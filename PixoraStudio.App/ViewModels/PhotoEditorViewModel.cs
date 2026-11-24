using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Collections.ObjectModel;
using PixoraStudio.Core.Imaging;
using PixoraStudio.Core.Geometry;
using PixoraStudio.App.Views;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixoraStudio.App.ViewModels;

public partial class PhotoEditorViewModel : ObservableObject, IDisposable
{
    private const int MaximumHistoryEntries = 50;
    private readonly IImageProcessingService _imageService;
    private readonly Stack<EditorSnapshot> _undoStack = new();
    private readonly Stack<EditorSnapshot> _redoStack = new();
    private readonly HashSet<PhotoLayerViewModel> _detachedLayers = new();
    private SKBitmap? _canvasBitmap;
    private SKBitmap? _processedBitmap;
    private SKColor _canvasBackgroundColor = SKColors.Transparent;
    private ImageAdjustment _lastCommittedAdjustment = new();
    private bool _isRestoringSnapshot;
    private EditorSnapshot? _pendingLayerGeometrySnapshot;
    private EditorSnapshot? _pendingAdjustmentSnapshot;
    private float _resizeAspectRatio = 1f;
    private PhotoLayerViewModel? _adjustmentLayer;

    [ObservableProperty] private string? imagePath;
    [ObservableProperty] private PhotoLayerViewModel? selectedLayer;
    public ObservableCollection<PhotoLayerViewModel> AdditionalLayers { get; } = [];
    public IReadOnlyList<string> FontFamilies { get; } = Fonts.SystemFontFamilies
        .Select(font => font.Source)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(font => font, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    [ObservableProperty] private BitmapSource? previewImage;
    [ObservableProperty] private bool isGpuPreviewAvailable;
    public event EventHandler? PreviewInvalidated;
    [ObservableProperty] private BitmapSource? layerDragPreviewImage;
    [ObservableProperty] private int layerDragPreviewX;
    [ObservableProperty] private int layerDragPreviewY;
    [ObservableProperty] private int layerDragPreviewWidth;
    [ObservableProperty] private int layerDragPreviewHeight;
    private bool _isLayerDragPreviewActive;
    [ObservableProperty] private float brightness;
    [ObservableProperty] private float contrast = 1f;
    [ObservableProperty] private float saturation = 1f;
    [ObservableProperty] private float exposure;
    [ObservableProperty] private float highlights;
    [ObservableProperty] private float shadows;
    [ObservableProperty] private float temperature;
    [ObservableProperty] private float tint;
    [ObservableProperty] private ImageFilter selectedFilter = ImageFilter.None;
    [ObservableProperty] private bool isSnappingEnabled = true;
    [ObservableProperty] private double zoom = 1.0;
    [ObservableProperty] private bool isImageLoaded;
    [ObservableProperty] private bool canUndo;
    [ObservableProperty] private bool canRedo;
    [ObservableProperty] private int imageWidth;
    [ObservableProperty] private int imageHeight;
    [ObservableProperty] private int cropX;
    [ObservableProperty] private int cropY;
    [ObservableProperty] private int cropWidth;
    [ObservableProperty] private int cropHeight;
    [ObservableProperty] private int resizeWidth;
    [ObservableProperty] private int resizeHeight;
    [ObservableProperty] private bool lockAspectRatio = true;
    [ObservableProperty] private bool isCropMode;
    [ObservableProperty] private string? operationError;
    [ObservableProperty] private float rotationAngle;
    [ObservableProperty] private string brushColor = "#FF000000";
    [ObservableProperty] private double brushSize = 12d;

    public PhotoEditorViewModel(IImageProcessingService imageService) => _imageService = imageService;

    public void CreateCanvas(int width, int height, SKColor backgroundColor)
    {
        var newCanvas = ImageDocumentCanvas.Create(width, height, backgroundColor);
        ClearHistory();
        ClearAdditionalLayers();
        DisposeBitmaps();
        _canvasBitmap = newCanvas;
        _canvasBackgroundColor = backgroundColor;
        _isLayerDragPreviewActive = false;
        LayerDragPreviewImage = null;
        ImagePath = null;
        IsImageLoaded = false;
        SetDefaultAdjustments();
        UpdateDimensions();
        IsImageLoaded = true;
        OperationError = null;
        RefreshPreview();
    }

    public void ResizeCanvas(int width, int height, bool centreContent)
    {
        if (_canvasBitmap is null)
            throw new InvalidOperationException("Create or open a canvas before resizing it.");

        var offset = CanvasResizeOffset.Calculate(
            ImageWidth, ImageHeight, width, height, centreContent);
        int offsetX = offset.X;
        int offsetY = offset.Y;
        var resizedCanvas = ImageDocumentCanvas.Resize(
            _canvasBitmap, width, height, offsetX, offsetY, _canvasBackgroundColor);

        var previousCanvas = _canvasBitmap;
        _canvasBitmap = resizedCanvas;
        previousCanvas.Dispose();

        foreach (var layer in AdditionalLayers)
        {
            layer.SetBounds(layer.X + offsetX, layer.Y + offsetY, layer.Width, layer.Height, ignoreLock: true);
        }

        UpdateDimensions();
        OperationError = null;
        RefreshPreview();
    }

    public void LoadImage(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ClearHistory();
        ClearAdditionalLayers();
        DisposeBitmaps();
        var firstImage = _imageService.LoadImage(filePath);
        _canvasBitmap = ImageDocumentCanvas.Create(firstImage.Width, firstImage.Height);
        _canvasBackgroundColor = SKColors.Transparent;
        ImagePath = filePath;
        IsImageLoaded = false;
        SetDefaultAdjustments();
        UpdateDimensions();
        var firstLayer = CreateLayer(Path.GetFileName(filePath), firstImage, 0, 0);
        AttachLayer(firstLayer);
        AdditionalLayers.Add(firstLayer);
        SelectedLayer = firstLayer;
        IsImageLoaded = true;
        RefreshPreview();
    }

    private ImageAdjustment GetCurrentAdjustment()
    {
        if (SelectedLayer is { IsLocked: true } lockedLayer)
            return lockedLayer.Adjustment;

        return GetAdjustmentValues();
    }

    private ImageAdjustment GetAdjustmentValues() => new()
    {
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
        Exposure = Exposure,
        Highlights = Highlights,
        Shadows = Shadows,
        Temperature = Temperature,
        Tint = Tint,
        Filter = SelectedFilter
    };

    private static bool AreAdjustmentsEqual(ImageAdjustment left, ImageAdjustment right) =>
        left.Brightness == right.Brightness &&
        left.Contrast == right.Contrast &&
        left.Saturation == right.Saturation &&
        left.Exposure == right.Exposure &&
        left.Highlights == right.Highlights &&
        left.Shadows == right.Shadows &&
        left.Temperature == right.Temperature &&
        left.Tint == right.Tint &&
        left.Filter == right.Filter;

    private void SaveUndoState()
    {
        if (SelectedLayer is null) return;
        PushHistory(_undoStack, CaptureSnapshot());
        ClearStack(_redoStack);
        DisposeUnreferencedDetachedLayers();
        UpdateHistoryState();
    }

    private EditorSnapshot CaptureSnapshot() => CaptureSnapshot(SelectedLayer!);

    private EditorSnapshot CaptureSnapshot(ImageAdjustment adjustment) =>
        CaptureLayerSnapshot(SelectedLayer!, adjustment, SelectedLayer);

    private EditorSnapshot CaptureSnapshot(PhotoLayerViewModel layer) =>
        CaptureLayerSnapshot(layer,
            ReferenceEquals(SelectedLayer, layer) ? GetCurrentAdjustment() : layer.Adjustment,
            SelectedLayer);

    private EditorSnapshot CaptureLayerSnapshot(PhotoLayerViewModel layer, bool isInLayerCollection,
        int layerIndex, PhotoLayerViewModel? selectedLayer) =>
        CaptureLayerSnapshot(layer,
            ReferenceEquals(SelectedLayer, layer) ? GetCurrentAdjustment() : layer.Adjustment,
            isInLayerCollection, layerIndex, selectedLayer);

    private EditorSnapshot CaptureLayerSnapshot(PhotoLayerViewModel layer, ImageAdjustment adjustment,
        PhotoLayerViewModel? selectedLayer)
    {
        var layerIndex = AdditionalLayers.IndexOf(layer);
        return CaptureLayerSnapshot(layer, adjustment, layerIndex >= 0, layerIndex, selectedLayer);
    }

    private EditorSnapshot CaptureLayerSnapshot(PhotoLayerViewModel layer, ImageAdjustment adjustment,
        bool isInLayerCollection, int layerIndex, PhotoLayerViewModel? selectedLayer) =>
        new(layer, _imageService.CloneImage(layer.Bitmap), adjustment,
            layer.X, layer.Y, layer.Width, layer.Height,
            isInLayerCollection, layerIndex, selectedLayer);

    public void BeginAdjustmentChange()
    {
        if (_pendingAdjustmentSnapshot is not null || !IsImageLoaded ||
            SelectedLayer is not { IsLocked: false })
            return;

        _pendingAdjustmentSnapshot = CaptureSnapshot();
    }

    public void CommitAdjustmentChange()
    {
        if (_pendingAdjustmentSnapshot is not { } snapshot)
            return;

        _pendingAdjustmentSnapshot = null;
        if (!AdditionalLayers.Contains(snapshot.Layer))
        {
            snapshot.Dispose();
            return;
        }

        RecordAdjustmentChange(snapshot, snapshot.Layer, GetAdjustmentValues());
    }

    private void OnAdjustmentValueChanged()
    {
        if (!IsImageLoaded)
            return;

        RefreshPreview();
        if (_isRestoringSnapshot || _pendingAdjustmentSnapshot is not null ||
            SelectedLayer is not { IsLocked: false } layer)
            return;

        var current = GetAdjustmentValues();
        if (AreAdjustmentsEqual(current, _lastCommittedAdjustment))
            return;

        RecordAdjustmentChange(CaptureSnapshot(_lastCommittedAdjustment), layer, current);
    }

    private void RecordAdjustmentChange(EditorSnapshot snapshot, PhotoLayerViewModel layer, ImageAdjustment current)
    {
        if (!AdditionalLayers.Contains(layer) || AreAdjustmentsEqual(snapshot.Adjustment, current))
        {
            snapshot.Dispose();
            return;
        }

        layer.Adjustment = current;
        if (ReferenceEquals(layer, SelectedLayer))
            _lastCommittedAdjustment = current;

        PushHistory(_undoStack, snapshot);
        ClearStack(_redoStack);
        DisposeUnreferencedDetachedLayers();
        UpdateHistoryState();
    }

    private void RecordLayerCollectionChange(EditorSnapshot snapshot)
    {
        PushHistory(_undoStack, snapshot);
        ClearStack(_redoStack);
        DisposeUnreferencedDetachedLayers();
        UpdateHistoryState();
    }

    public void BeginLayerGeometryChange()
    {
        if (SelectedLayer is null || SelectedLayer.IsLocked || _pendingLayerGeometrySnapshot is not null)
            return;
        _pendingLayerGeometrySnapshot = CaptureSnapshot();
    }

    public void CommitLayerGeometryChange()
    {
        if (_pendingLayerGeometrySnapshot is not { } snapshot)
            return;

        _pendingLayerGeometrySnapshot = null;
        var layer = snapshot.Layer;
        bool changed = layer.X != snapshot.X || layer.Y != snapshot.Y ||
                       layer.Width != snapshot.Width || layer.Height != snapshot.Height;
        if (!changed)
        {
            snapshot.Dispose();
            return;
        }

        PushHistory(_undoStack, snapshot);
        ClearStack(_redoStack);
        DisposeUnreferencedDetachedLayers();
        UpdateHistoryState();
    }

    private void PushHistory(Stack<EditorSnapshot> stack, EditorSnapshot snapshot)
    {
        stack.Push(snapshot);
        while (stack.Count > MaximumHistoryEntries)
        {
            var retained = stack.Reverse().Skip(Math.Max(0, stack.Count - MaximumHistoryEntries)).ToArray();
            var retainedSet = retained.ToHashSet();
            while (stack.TryPop(out var entry))
            {
                if (!retainedSet.Contains(entry))
                    entry.Dispose();
            }
            foreach (var entry in retained) stack.Push(entry);
        }
        DisposeUnreferencedDetachedLayers();
    }

    private static void ClearStack(Stack<EditorSnapshot> stack)
    {
        while (stack.TryPop(out var snapshot)) snapshot.Dispose();
    }

    private void DisposeUnreferencedDetachedLayers()
    {
        foreach (var layer in _detachedLayers.ToArray())
        {
            if (AdditionalLayers.Contains(layer))
            {
                _detachedLayers.Remove(layer);
                continue;
            }

            bool isReferenced = _undoStack.Any(snapshot =>
                                    ReferenceEquals(snapshot.Layer, layer) ||
                                    ReferenceEquals(snapshot.SelectedLayerAtCapture, layer)) ||
                                _redoStack.Any(snapshot =>
                                    ReferenceEquals(snapshot.Layer, layer) ||
                                    ReferenceEquals(snapshot.SelectedLayerAtCapture, layer)) ||
                                ReferenceEquals(_pendingLayerGeometrySnapshot?.Layer, layer) ||
                                ReferenceEquals(_pendingLayerGeometrySnapshot?.SelectedLayerAtCapture, layer) ||
                                ReferenceEquals(_pendingAdjustmentSnapshot?.Layer, layer) ||
                                ReferenceEquals(_pendingAdjustmentSnapshot?.SelectedLayerAtCapture, layer);
            if (isReferenced) continue;

            _detachedLayers.Remove(layer);
            layer.Dispose();
        }
    }

    private void ClearHistory()
    {
        _pendingLayerGeometrySnapshot?.Dispose();
        _pendingLayerGeometrySnapshot = null;
        _pendingAdjustmentSnapshot?.Dispose();
        _pendingAdjustmentSnapshot = null;
        ClearStack(_undoStack);
        ClearStack(_redoStack);
        foreach (var layer in _detachedLayers)
            layer.Dispose();
        _detachedLayers.Clear();
        UpdateHistoryState();
    }

    private void UpdateHistoryState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        var layer = snapshot.Layer;
        bool wasRestoringSnapshot = _isRestoringSnapshot;
        _isRestoringSnapshot = true;
        try
        {
            int currentIndex = AdditionalLayers.IndexOf(layer);
            if (snapshot.IsInLayerCollection)
            {
                if (currentIndex < 0)
                {
                    _detachedLayers.Remove(layer);
                    AttachLayer(layer);
                    int insertIndex = Math.Clamp(snapshot.LayerIndex, 0, AdditionalLayers.Count);
                    AdditionalLayers.Insert(insertIndex, layer);
                }
                else
                {
                    int targetIndex = Math.Clamp(snapshot.LayerIndex, 0, AdditionalLayers.Count - 1);
                    if (currentIndex != targetIndex)
                        AdditionalLayers.Move(currentIndex, targetIndex);
                }
            }
            else
            {
                if (currentIndex >= 0)
                {
                    AdditionalLayers.RemoveAt(currentIndex);
                    DetachLayer(layer);
                }
                _detachedLayers.Add(layer);
            }

            var selectedLayer = snapshot.SelectedLayerAtCapture is { } previousSelection &&
                                AdditionalLayers.Contains(previousSelection)
                ? previousSelection
                : null;
            if (!ReferenceEquals(SelectedLayer, selectedLayer))
                SelectedLayer = selectedLayer;

            layer.ReplaceBitmap(_imageService.CloneImage(snapshot.Bitmap), ignoreLock: true);
            layer.SetBounds(snapshot.X, snapshot.Y, snapshot.Width, snapshot.Height, ignoreLock: true);
            layer.Adjustment = snapshot.Adjustment;

            if (ReferenceEquals(SelectedLayer, layer))
            {
                Brightness = snapshot.Adjustment.Brightness;
                Contrast = snapshot.Adjustment.Contrast;
                Saturation = snapshot.Adjustment.Saturation;
                Exposure = snapshot.Adjustment.Exposure;
                Highlights = snapshot.Adjustment.Highlights;
                Shadows = snapshot.Adjustment.Shadows;
                Temperature = snapshot.Adjustment.Temperature;
                Tint = snapshot.Adjustment.Tint;
                SelectedFilter = snapshot.Adjustment.Filter;
                _lastCommittedAdjustment = GetCurrentAdjustment();

                CropX = CropY = 0;
                CropWidth = ResizeWidth = layer.Bitmap.Width;
                CropHeight = ResizeHeight = layer.Bitmap.Height;
                _resizeAspectRatio = (float)layer.Bitmap.Width / Math.Max(1, layer.Bitmap.Height);
            }
        }
        finally
        {
            _isRestoringSnapshot = wasRestoringSnapshot;
            snapshot.Dispose();
        }

        RefreshPreview();
        DisposeUnreferencedDetachedLayers();
    }

    private static SKBitmap CreateLayerRenderBitmap(PhotoLayerViewModel layer)
    {
        if (layer.ContentType is PhotoLayerContentType.Image or PhotoLayerContentType.Empty) return layer.Bitmap;
        return LayerContentRenderer.Render(layer.Width, layer.Height, layer.ContentType,
            layer.TextContent, layer.FontFamily, layer.FontSize, layer.FillColor,
            layer.StrokeColor, layer.StrokeWidth, layer.ShapeType, layer.TextAlignment);
    }

    private void RefreshPreview()
    {
        if (_canvasBitmap is null) return;

        if (IsGpuPreviewAvailable)
        {
            PreviewInvalidated?.Invoke(this, EventArgs.Empty);
            return;
        }

        BuildCpuPreview();
    }

    private void BuildCpuPreview()
    {
        if (_canvasBitmap is null) return;
        var renderedLayers = new List<SKBitmap>(AdditionalLayers.Count);
        try
        {
            var entries = new List<(SKBitmap Bitmap, int X, int Y, int Width, int Height, bool IsVisible, float Opacity)>(AdditionalLayers.Count);
            foreach (var layer in AdditionalLayers.Reverse())
            {
                var source = CreateLayerRenderBitmap(layer);
                var adjustment = ReferenceEquals(layer, SelectedLayer)
                    ? GetCurrentAdjustment()
                    : layer.Adjustment;
                var rendered = _imageService.ApplyAdjustments(source, adjustment);
                if (!ReferenceEquals(source, layer.Bitmap)) source.Dispose();
                renderedLayers.Add(rendered);
                entries.Add((rendered, layer.X, layer.Y, layer.Width, layer.Height, layer.IsVisible, layer.Opacity));
            }
            _processedBitmap?.Dispose();
            _processedBitmap = ImageLayerCompositor.CompositeWithOpacity(_canvasBitmap, entries);
            PreviewImage = ConvertToBitmapSource(_processedBitmap);
        }
        finally { foreach (var bitmap in renderedLayers) bitmap.Dispose(); }
    }

    /// <summary>
    /// Draws the document directly into the caller's Skia canvas. When the caller
    /// supplies SKGLElement's OpenGL-backed canvas, layer uploads, adjustment
    /// shaders, scaling and alpha compositing are performed by the GPU.
    /// </summary>
    public void RenderGpuPreview(SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_canvasBitmap is null || !IsImageLoaded)
        {
            canvas.Clear(SKColors.Transparent);
            return;
        }

        var layers = new List<GpuImageLayer>(AdditionalLayers.Count);
        var generated = new List<SKBitmap>();
        try
        {
            foreach (var layer in AdditionalLayers.Reverse())
            {
                var source = CreateLayerRenderBitmap(layer);
                if (!ReferenceEquals(source, layer.Bitmap)) generated.Add(source);
                var adjustment = ReferenceEquals(layer, SelectedLayer)
                    ? GetCurrentAdjustment()
                    : layer.Adjustment;
                layers.Add(new GpuImageLayer(source, layer.X, layer.Y, layer.Width, layer.Height,
                    layer.IsVisible, adjustment, layer.Opacity));
            }
            GpuImageRenderer.Render(canvas, ImageWidth, ImageHeight, layers,
                checkerboardBackground: true, documentBackground: _canvasBitmap);
        }
        finally { foreach (var bitmap in generated) bitmap.Dispose(); }
    }

    /// <summary>Switches between GPU preview rendering and the CPU fallback.</summary>
    public void SetGpuPreviewAvailable(bool available)
    {
        if (IsGpuPreviewAvailable == available) return;
        IsGpuPreviewAvailable = available;
        if (!available)
        {
            _isLayerDragPreviewActive = false;
            BuildCpuPreview();
        }
        PreviewInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private static BitmapSource ConvertToBitmapSource(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        var result = new BitmapImage();
        result.BeginInit();
        result.CacheOption = BitmapCacheOption.OnLoad;
        result.StreamSource = stream;
        result.EndInit();
        result.Freeze();
        return result;
    }

    private void UpdateDimensions()
    {
        if (_canvasBitmap is null) return;
        ImageWidth = _canvasBitmap.Width;
        ImageHeight = _canvasBitmap.Height;
        CropX = CropY = 0;

        // Canvas dimensions and selected-layer dimensions are independent.
        // Keep the Resize tool focused on the selected layer after a canvas resize.
        if (SelectedLayer is { } layer)
        {
            CropWidth = ResizeWidth = layer.Width;
            CropHeight = ResizeHeight = layer.Height;
            _resizeAspectRatio = layer.Height == 0 ? 1f : (float)layer.Width / layer.Height;
        }
        else
        {
            CropWidth = ResizeWidth = ImageWidth;
            CropHeight = ResizeHeight = ImageHeight;
            _resizeAspectRatio = ImageHeight == 0 ? 1f : (float)ImageWidth / ImageHeight;
        }
    }

    private void ApplyTransform(Func<SKBitmap, SKBitmap> transform)
    {
        if (SelectedLayer is null || SelectedLayer.IsLocked) return;
        try
        {
            using var transformed = transform(SelectedLayer.Bitmap);
            SaveUndoState();
            SelectedLayer.ReplaceBitmap(_imageService.CloneImage(transformed));
            SelectedLayer.Adjustment = GetCurrentAdjustment();
            _lastCommittedAdjustment = GetCurrentAdjustment();
            OperationError = null;
            CropX = CropY = 0;
            CropWidth = ResizeWidth = SelectedLayer.Bitmap.Width;
            CropHeight = ResizeHeight = SelectedLayer.Bitmap.Height;
            _resizeAspectRatio = (float)SelectedLayer.Bitmap.Width / Math.Max(1, SelectedLayer.Bitmap.Height);
            RefreshPreview();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            OperationError = ex.Message;
        }
    }

    partial void OnBrightnessChanged(float value) => OnAdjustmentValueChanged();
    partial void OnContrastChanged(float value) => OnAdjustmentValueChanged();
    partial void OnSaturationChanged(float value) => OnAdjustmentValueChanged();
    partial void OnExposureChanged(float value) => OnAdjustmentValueChanged();
    partial void OnHighlightsChanged(float value) => OnAdjustmentValueChanged();
    partial void OnShadowsChanged(float value) => OnAdjustmentValueChanged();
    partial void OnTemperatureChanged(float value) => OnAdjustmentValueChanged();
    partial void OnTintChanged(float value) => OnAdjustmentValueChanged();
    partial void OnSelectedFilterChanged(ImageFilter value) => OnAdjustmentValueChanged();

    partial void OnResizeWidthChanged(int value)
    {
        if (!LockAspectRatio || _isRestoringSnapshot || value <= 0) return;
        ResizeHeight = Math.Max(1, (int)Math.Round(value / _resizeAspectRatio));
    }

    partial void OnResizeHeightChanged(int value)
    {
        if (!LockAspectRatio || _isRestoringSnapshot || value <= 0) return;
        ResizeWidth = Math.Max(1, (int)Math.Round(value * _resizeAspectRatio));
    }

    [RelayCommand]
    private void NewCanvas()
    {
        var result = CanvasDialogWindow.ShowNewCanvas(System.Windows.Application.Current?.MainWindow);
        if (result is null) return;

        try
        {
            CreateCanvas(result.Width, result.Height, result.BackgroundColor);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            OperationError = $"Unable to create canvas: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResizeCanvas()
    {
        if (_canvasBitmap is null) return;
        var result = CanvasDialogWindow.ShowResizeCanvas(
            System.Windows.Application.Current?.MainWindow, ImageWidth, ImageHeight);
        if (result is null) return;

        try
        {
            ResizeCanvas(result.Width, result.Height, result.CentreContent);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            OperationError = $"Unable to resize canvas: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open image",
            Filter = "Supported images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|" +
                     "PNG images|*.png|JPEG images|*.jpg;*.jpeg|" +
                     "WebP images|*.webp|Bitmap images|*.bmp|GIF images|*.gif|" +
                     "All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            LoadImage(dialog.FileName);
            OperationError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or InvalidOperationException)
        {
            OperationError = $"Unable to open image: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddLayer()
    {
        if (_canvasBitmap is null) return;
        CommitAdjustmentChange();
        CommitLayerGeometryChange();
        var type = AddLayerDialogWindow.Show(System.Windows.Application.Current?.MainWindow);
        if (type is null) return;
        if (type == PhotoLayerContentType.Image) { AddImageLayer(); return; }
        try
        {
            int width = type == PhotoLayerContentType.Text ? 520 : type == PhotoLayerContentType.Empty ? 320 : 240;
            int height = type == PhotoLayerContentType.Text ? 120 : type == PhotoLayerContentType.Empty ? 240 : 180;
            using var initial = LayerContentRenderer.Render(width, height, type.Value, text: "Your text",
                fontSize: 64, fillColor: "#FFFFFFFF", strokeColor: "#FFFFFFFF",
                strokeWidth: type == PhotoLayerContentType.Frame ? 8 : 0);
            var layer = CreateLayer(type.Value + " Layer", _imageService.CloneImage(initial),
                (_canvasBitmap.Width - width) / 2, (_canvasBitmap.Height - height) / 2);
            layer.ContentType = type.Value;
            layer.TextContent = "Your text"; layer.FontSize = 64;
            layer.FillColor = "#FFFFFFFF"; layer.StrokeColor = "#FF202020";
            layer.StrokeWidth = type == PhotoLayerContentType.Frame ? 8 : 0;
            var history = CaptureLayerSnapshot(layer, isInLayerCollection: false, layerIndex: 0, selectedLayer: SelectedLayer);
            AttachLayer(layer);
            AdditionalLayers.Insert(0, layer);
            RecordLayerCollectionChange(history);
            SelectedLayer = layer;
            OperationError = null;
            RefreshPreview();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        { OperationError = $"Unable to add layer: {ex.Message}"; }
    }

    [RelayCommand]
    private void AddImageLayer()
    {
        if (_canvasBitmap is null) return;
        CommitAdjustmentChange();
        CommitLayerGeometryChange();
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add image layer",
            Filter = "Supported images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var bitmap = _imageService.LoadImage(dialog.FileName);
            var layer = CreateLayer(Path.GetFileName(dialog.FileName), bitmap,
                (_canvasBitmap.Width - bitmap.Width) / 2, (_canvasBitmap.Height - bitmap.Height) / 2);
            var history = CaptureLayerSnapshot(layer, isInLayerCollection: false, layerIndex: 0, selectedLayer: SelectedLayer);
            AttachLayer(layer);
            AdditionalLayers.Insert(0, layer);
            RecordLayerCollectionChange(history);
            SelectedLayer = layer;
            OperationError = null;
            RefreshPreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            OperationError = $"Unable to add image layer: {ex.Message}";
        }
    }

    public void AddBrushStroke(IReadOnlyList<SKPoint> points)
    {
        var canvasBitmap = _canvasBitmap;
        if (canvasBitmap is null || points.Count == 0)
            return;

        CommitAdjustmentChange();
        CommitLayerGeometryChange();

        SKBitmap? strokeBitmap = null;
        PhotoLayerViewModel? layer = null;
        EditorSnapshot? history = null;
        try
        {
            float brushSize = (float)Math.Clamp(double.IsFinite(BrushSize) ? BrushSize : 12d, 1d, 100d);
            var color = SKColor.Parse(BrushColor);
            var clippedPoints = points.Select(point => new SKPoint(
                Math.Clamp(point.X, 0, canvasBitmap.Width),
                Math.Clamp(point.Y, 0, canvasBitmap.Height))).ToArray();

            float minX = clippedPoints.Min(point => point.X);
            float minY = clippedPoints.Min(point => point.Y);
            float maxX = clippedPoints.Max(point => point.X);
            float maxY = clippedPoints.Max(point => point.Y);
            float margin = brushSize / 2f + 1f;
            int left = Math.Clamp((int)MathF.Floor(minX - margin), 0, canvasBitmap.Width - 1);
            int top = Math.Clamp((int)MathF.Floor(minY - margin), 0, canvasBitmap.Height - 1);
            int right = Math.Clamp((int)MathF.Ceiling(maxX + margin), left + 1, canvasBitmap.Width);
            int bottom = Math.Clamp((int)MathF.Ceiling(maxY + margin), top + 1, canvasBitmap.Height);

            strokeBitmap = new SKBitmap(right - left, bottom - top);
            var localPoints = clippedPoints
                .Select(point => new SKPoint(point.X - left, point.Y - top))
                .ToArray();

            using (var canvas = new SKCanvas(strokeBitmap))
            using (var paint = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                StrokeWidth = brushSize,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                Style = SKPaintStyle.Stroke
            })
            {
                canvas.Clear(SKColors.Transparent);
                bool hasMovement = localPoints.Skip(1).Any(point =>
                    Math.Abs(point.X - localPoints[0].X) > 0.01f ||
                    Math.Abs(point.Y - localPoints[0].Y) > 0.01f);
                if (hasMovement)
                {
                    canvas.DrawPoints(SKPointMode.Polygon, localPoints, paint);
                }
                else
                {
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(localPoints[0].X, localPoints[0].Y, brushSize / 2f, paint);
                }
                canvas.Flush();
            }

            var ownedBitmap = strokeBitmap ?? throw new InvalidOperationException("Unable to create the brush stroke image.");
            strokeBitmap = null;
            layer = CreateLayer("Brush stroke", ownedBitmap, left, top);
            history = CaptureLayerSnapshot(layer, isInLayerCollection: false,
                layerIndex: 0, selectedLayer: SelectedLayer);
            AttachLayer(layer);
            AdditionalLayers.Insert(0, layer);
            RecordLayerCollectionChange(history);
            history = null;
            SelectedLayer = layer;
            OperationError = null;
            RefreshPreview();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                   OutOfMemoryException or FormatException)
        {
            strokeBitmap?.Dispose();
            history?.Dispose();
            if (layer is not null && !AdditionalLayers.Contains(layer))
            {
                DetachLayer(layer);
                layer.Dispose();
            }
            OperationError = $"Unable to draw brush stroke: {ex.Message}";
        }
    }

    public void CopyImageToClipboard()
    {
        try
        {
            BitmapSource clipboardImage;
            if (SelectedLayer is { } layer)
            {
                using var renderedLayer = RenderLayerForClipboard(layer);
                clipboardImage = ConvertToBitmapSource(renderedLayer);
            }
            else
            {
                if (_canvasBitmap is null) return;
                BuildCpuPreview();
                if (_processedBitmap is null) return;
                clipboardImage = ConvertToBitmapSource(_processedBitmap);
            }

            Clipboard.SetImage(clipboardImage);
            OperationError = null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                   OutOfMemoryException or ExternalException)
        {
            OperationError = $"Unable to copy image: {ex.Message}";
        }
    }

    public void PasteImageFromClipboard()
    {
        CommitAdjustmentChange();
        CommitLayerGeometryChange();

        try
        {
            bool pastedImage = false;
            if (Clipboard.ContainsFileDropList())
            {
                foreach (string filePath in Clipboard.GetFileDropList().Cast<string>())
                {
                    if (!File.Exists(filePath) || !IsSupportedClipboardImage(filePath))
                        continue;

                    try
                    {
                        var bitmap = _imageService.LoadImage(filePath);
                        AddPastedImageLayer(Path.GetFileName(filePath), bitmap);
                        pastedImage = true;
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                               IOException or UnauthorizedAccessException or OutOfMemoryException)
                    {
                        OperationError = $"Unable to paste {Path.GetFileName(filePath)}: {ex.Message}";
                    }
                }
            }

            if (!pastedImage)
            {
                var source = Clipboard.GetImage();
                if (source is not null)
                {
                    using var stream = new MemoryStream();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    encoder.Save(stream);
                    stream.Position = 0;

                    var bitmap = SKBitmap.Decode(stream)
                        ?? throw new InvalidOperationException("The clipboard image could not be decoded.");
                    AddPastedImageLayer("Pasted Image", bitmap);
                    pastedImage = true;
                }
            }

            if (!pastedImage) return;
            OperationError = null;
            RefreshPreview();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                   IOException or UnauthorizedAccessException or OutOfMemoryException or
                                   ExternalException)
        {
            OperationError = $"Unable to paste image: {ex.Message}";
        }
    }

    private SKBitmap RenderLayerForClipboard(PhotoLayerViewModel layer)
    {
        var source = CreateLayerRenderBitmap(layer);
        try
        {
            var adjustment = ReferenceEquals(layer, SelectedLayer)
                ? GetCurrentAdjustment()
                : layer.Adjustment;
            using var adjusted = _imageService.ApplyAdjustments(source, adjustment);
            var rendered = new SKBitmap(Math.Max(1, layer.Width), Math.Max(1, layer.Height));
            using (var canvas = new SKCanvas(rendered))
            using (var paint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, (byte)Math.Round(layer.Opacity * byte.MaxValue))
            })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(adjusted, new SKRect(0, 0, rendered.Width, rendered.Height), paint);
                canvas.Flush();
            }
            return rendered;
        }
        finally
        {
            if (!ReferenceEquals(source, layer.Bitmap))
                source.Dispose();
        }
    }

    private void AddPastedImageLayer(string name, SKBitmap bitmap)
    {
        if (_canvasBitmap is null)
            CreateCanvas(bitmap.Width, bitmap.Height, SKColors.Transparent);

        var selected = SelectedLayer;
        int x = selected is null ? (_canvasBitmap.Width - bitmap.Width) / 2 : selected.X + 16;
        int y = selected is null ? (_canvasBitmap.Height - bitmap.Height) / 2 : selected.Y + 16;
        var layer = CreateLayer(name, bitmap, x, y);
        var history = CaptureLayerSnapshot(layer, isInLayerCollection: false,
            layerIndex: 0, selectedLayer: SelectedLayer);
        AttachLayer(layer);
        AdditionalLayers.Insert(0, layer);
        RecordLayerCollectionChange(history);
        SelectedLayer = layer;
    }

    private static bool IsSupportedClipboardImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";

    [RelayCommand]
    private void RemoveLayer(PhotoLayerViewModel? layer)
    {
        if (layer is null || !AdditionalLayers.Contains(layer))
            return;

        CommitAdjustmentChange();
        CommitLayerGeometryChange();

        int layerIndex = AdditionalLayers.IndexOf(layer);
        bool removingSelected = ReferenceEquals(SelectedLayer, layer);
        var history = CaptureLayerSnapshot(layer, isInLayerCollection: true, layerIndex: layerIndex, selectedLayer: SelectedLayer);

        if (!AdditionalLayers.Remove(layer))
        {
            history.Dispose();
            return;
        }

        DetachLayer(layer);
        _detachedLayers.Add(layer);
        RecordLayerCollectionChange(history);
        if (removingSelected)
            SelectedLayer = AdditionalLayers.FirstOrDefault();

        RefreshPreview();
    }

    [RelayCommand]
    private void DuplicateLayer(PhotoLayerViewModel? layer)
    {
        if (layer is null || !AdditionalLayers.Contains(layer) || _canvasBitmap is null)
            return;

        CommitAdjustmentChange();
        CommitLayerGeometryChange();
        try
        {
            var duplicate = CreateLayer($"{layer.Name} Copy", _imageService.CloneImage(layer.Bitmap),
                layer.X + 16, layer.Y + 16);
            duplicate.Adjustment = new ImageAdjustment
            {
                Brightness = layer.Adjustment.Brightness,
                Contrast = layer.Adjustment.Contrast,
                Saturation = layer.Adjustment.Saturation,
                Exposure = layer.Adjustment.Exposure,
                Highlights = layer.Adjustment.Highlights,
                Shadows = layer.Adjustment.Shadows,
                Temperature = layer.Adjustment.Temperature,
                Tint = layer.Adjustment.Tint,
                Filter = layer.Adjustment.Filter
            };
            duplicate.Opacity = layer.Opacity;
            duplicate.IsVisible = layer.IsVisible;
            duplicate.ContentType = layer.ContentType;
            duplicate.TextContent = layer.TextContent; duplicate.TextAlignment = layer.TextAlignment; duplicate.FontFamily = layer.FontFamily;
            duplicate.FontSize = layer.FontSize; duplicate.FillColor = layer.FillColor;
            duplicate.StrokeColor = layer.StrokeColor; duplicate.StrokeWidth = layer.StrokeWidth;
            duplicate.ShapeType = layer.ShapeType;
            int insertIndex = AdditionalLayers.IndexOf(layer);
            var history = CaptureLayerSnapshot(duplicate, isInLayerCollection: false, layerIndex: insertIndex, selectedLayer: SelectedLayer);
            AttachLayer(duplicate);
            AdditionalLayers.Insert(insertIndex, duplicate);
            RecordLayerCollectionChange(history);
            SelectedLayer = duplicate;
            RefreshPreview();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            OperationError = $"Unable to duplicate layer: {ex.Message}";
        }
    }

    public void MoveLayer(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= AdditionalLayers.Count ||
            newIndex < 0 || newIndex >= AdditionalLayers.Count || oldIndex == newIndex)
            return;

        AdditionalLayers.Move(oldIndex, newIndex);
        RefreshPreview();
    }

    private void ClearAdditionalLayers()
    {
        foreach (var layer in AdditionalLayers)
        {
            DetachLayer(layer);
            layer.Dispose();
        }
        AdditionalLayers.Clear();
        foreach (var layer in _detachedLayers)
            layer.Dispose();
        _detachedLayers.Clear();
        SelectedLayer = null;
    }

    private PhotoLayerViewModel CreateLayer(string name, SKBitmap bitmap, int x, int y)
    {
        try
        {
            var thumbnailBitmap = _imageService.Resize(bitmap, 64,
                Math.Max(1, (int)Math.Round(64d * bitmap.Height / bitmap.Width)));
            BitmapSource thumbnail;
            try { thumbnail = ConvertToBitmapSource(thumbnailBitmap); }
            finally { thumbnailBitmap.Dispose(); }
            return new PhotoLayerViewModel(name, bitmap, thumbnail, x, y);
        }
        catch { bitmap.Dispose(); throw; }
    }

    private void AttachLayer(PhotoLayerViewModel layer)
    {
        layer.VisibilityChanged += OnLayerVisibilityChanged;
        layer.GeometryChanged += OnLayerGeometryChanged;
        layer.AppearanceChanged += OnLayerAppearanceChanged;
        layer.ContentChanged += OnLayerContentChanged;
    }

    private void DetachLayer(PhotoLayerViewModel layer)
    {
        layer.VisibilityChanged -= OnLayerVisibilityChanged;
        layer.GeometryChanged -= OnLayerGeometryChanged;
        layer.AppearanceChanged -= OnLayerAppearanceChanged;
        layer.ContentChanged -= OnLayerContentChanged;
    }

    private void OnLayerVisibilityChanged(object? sender, EventArgs e) => RefreshPreview();

    private void OnLayerAppearanceChanged(object? sender, EventArgs e) => RefreshPreview();
    private void OnLayerContentChanged(object? sender, EventArgs e) => RefreshPreview();

    private void OnLayerGeometryChanged(object? sender, EventArgs e)
    {
        if (!_isLayerDragPreviewActive)
            RefreshPreview();
    }

    public void BeginLayerDragPreview()
    {
        if (_canvasBitmap is null || SelectedLayer is not { IsVisible: true } selected ||
            _isLayerDragPreviewActive)
            return;

        if (IsGpuPreviewAvailable)
        {
            _isLayerDragPreviewActive = true;
            LayerDragPreviewImage = null;
            PreviewInvalidated?.Invoke(this, EventArgs.Empty);
            return;
        }

        var renderedLayers = new List<SKBitmap>(Math.Max(0, AdditionalLayers.Count - 1));
        try
        {
            var entries = new List<(SKBitmap Bitmap, int X, int Y, int Width, int Height, bool IsVisible, float Opacity)>(
                Math.Max(0, AdditionalLayers.Count - 1));

            foreach (var layer in AdditionalLayers.Reverse())
            {
                if (ReferenceEquals(layer, selected) || !layer.IsVisible)
                    continue;

                var adjustment = layer.Adjustment;
                var rendered = _imageService.ApplyAdjustments(layer.Bitmap, adjustment);
                renderedLayers.Add(rendered);
                entries.Add((rendered, layer.X, layer.Y, layer.Width, layer.Height, true, layer.Opacity));
            }

            using var baseComposite = ImageLayerCompositor.CompositeWithOpacity(_canvasBitmap, entries);
            var selectedAdjustment = GetCurrentAdjustment();
            using var selectedBitmap = _imageService.ApplyAdjustments(selected.Bitmap, selectedAdjustment);

            PreviewImage = ConvertToBitmapSource(baseComposite);
            LayerDragPreviewImage = ConvertToBitmapSource(selectedBitmap);
            LayerDragPreviewX = selected.X;
            LayerDragPreviewY = selected.Y;
            LayerDragPreviewWidth = selected.Width;
            LayerDragPreviewHeight = selected.Height;
            _isLayerDragPreviewActive = true;
        }
        finally
        {
            foreach (var bitmap in renderedLayers)
                bitmap.Dispose();
        }
    }

    public void SetLayerDragPreviewBounds(int x, int y, int width, int height)
    {
        if (!_isLayerDragPreviewActive)
            return;

        LayerDragPreviewX = x;
        LayerDragPreviewY = y;
        LayerDragPreviewWidth = width;
        LayerDragPreviewHeight = height;
        if (IsGpuPreviewAvailable)
            PreviewInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public void EndLayerDragPreview()
    {
        if (!_isLayerDragPreviewActive)
            return;

        _isLayerDragPreviewActive = false;
        LayerDragPreviewImage = null;
        RefreshPreview();
    }

    public void SelectLayer(PhotoLayerViewModel? layer)
    {
        if (layer is not null && !AdditionalLayers.Contains(layer)) return;
        SelectedLayer = layer;
    }

    public PhotoLayerViewModel? HitTestLayer(double x, double y)
    {
        foreach (var layer in AdditionalLayers)
        {
            if (!layer.IsVisible) continue;
            if (x >= layer.X && x <= layer.X + layer.Width && y >= layer.Y && y <= layer.Y + layer.Height)
                return layer;
        }
        return null;
    }

    partial void OnSelectedLayerChanged(PhotoLayerViewModel? value)
    {
        CommitAdjustmentChange();
        if (_adjustmentLayer is not null)
            _adjustmentLayer.Adjustment = GetAdjustmentValues();
        _adjustmentLayer = value;
        foreach (var layer in AdditionalLayers) layer.IsSelected = ReferenceEquals(layer, value);
        if (value is null) return;
        bool wasRestoringSnapshot = _isRestoringSnapshot;
        _isRestoringSnapshot = true;
        try
        {
            Brightness = value.Adjustment.Brightness;
            Contrast = value.Adjustment.Contrast;
            Saturation = value.Adjustment.Saturation;
            Exposure = value.Adjustment.Exposure;
            Highlights = value.Adjustment.Highlights;
            Shadows = value.Adjustment.Shadows;
            Temperature = value.Adjustment.Temperature;
            Tint = value.Adjustment.Tint;
            SelectedFilter = value.Adjustment.Filter;
            _lastCommittedAdjustment = GetCurrentAdjustment();
            _resizeAspectRatio = (float)value.Bitmap.Width / Math.Max(1, value.Bitmap.Height);
            ResizeWidth = value.Bitmap.Width;
            ResizeHeight = value.Bitmap.Height;
            CropX = CropY = 0;
            CropWidth = value.Bitmap.Width;
            CropHeight = value.Bitmap.Height;
        }
        finally { _isRestoringSnapshot = wasRestoringSnapshot; }
        if (IsImageLoaded) RefreshPreview();
    }

    [RelayCommand]
    private void CommitAdjustments() => CommitAdjustmentChange();

    [RelayCommand] private void RotateClockwise() => ApplyTransform(bitmap => _imageService.Rotate(bitmap, 90));
    [RelayCommand] private void RotateCounterClockwise() => ApplyTransform(bitmap => _imageService.Rotate(bitmap, 270));
    [RelayCommand] private void Rotate180() => ApplyTransform(bitmap => _imageService.Rotate(bitmap, 180));

    [RelayCommand]
    private void ApplyRotation()
    {
        if (!IsImageLoaded || Math.Abs(RotationAngle) < 0.01f) return;
        float angle = RotationAngle;
        ApplyTransform(bitmap => _imageService.Rotate(bitmap, angle));
        if (OperationError is null) RotationAngle = 0;
    }

    [RelayCommand]
    private void ResetRotation() => RotationAngle = 0;
    [RelayCommand] private void FlipHorizontal() => ApplyTransform(bitmap => _imageService.Flip(bitmap, true, false));
    [RelayCommand] private void FlipVertical() => ApplyTransform(bitmap => _imageService.Flip(bitmap, false, true));

    [RelayCommand]
    private void Crop()
    {
        if (SelectedLayer is null) return;
        ApplyTransform(bitmap => _imageService.Crop(bitmap,
            new SKRectI(CropX, CropY, checked(CropX + CropWidth), checked(CropY + CropHeight))));
        IsCropMode = false;
    }

    [RelayCommand]
    private void Resize()
    {
        if (SelectedLayer is null) return;
        ApplyTransform(bitmap => _imageService.Resize(bitmap, ResizeWidth, ResizeHeight));
    }

    [RelayCommand]
    private void Undo()
    {
        CommitAdjustmentChange();
        CommitLayerGeometryChange();
        if (_undoStack.Count == 0) return;

        var snapshot = _undoStack.Pop();
        PushHistory(_redoStack, CaptureSnapshot(snapshot.Layer));
        RestoreSnapshot(snapshot);
        UpdateHistoryState();
    }

    [RelayCommand]
    private void Redo()
    {
        CommitAdjustmentChange();
        CommitLayerGeometryChange();
        if (_redoStack.Count == 0) return;

        var snapshot = _redoStack.Pop();
        PushHistory(_undoStack, CaptureSnapshot(snapshot.Layer));
        RestoreSnapshot(snapshot);
        UpdateHistoryState();
    }

    private void SetDefaultAdjustments()
    {
        _isRestoringSnapshot = true;
        try
        {
            Brightness = 0f;
            Contrast = 1f;
            Saturation = 1f;
            Exposure = 0f;
            Highlights = 0f;
            Shadows = 0f;
            Temperature = 0f;
            Tint = 0f;
            SelectedFilter = ImageFilter.None;
        }
        finally { _isRestoringSnapshot = false; }
        _lastCommittedAdjustment = GetCurrentAdjustment();
    }

    [RelayCommand]
    private void ResetAdjustments()
    {
        if (!IsImageLoaded || SelectedLayer is null || SelectedLayer.IsLocked) return;
        var current = GetCurrentAdjustment();
        if (current.Brightness == 0f && current.Contrast == 1f &&
            current.Saturation == 1f && current.Exposure == 0f &&
            current.Highlights == 0f && current.Shadows == 0f &&
            current.Temperature == 0f && current.Tint == 0f &&
            current.Filter == ImageFilter.None) return;

        SaveUndoState();
        SetDefaultAdjustments();
        if (SelectedLayer is not null) SelectedLayer.Adjustment = GetCurrentAdjustment();
        ClearStack(_redoStack);
        DisposeUnreferencedDetachedLayers();
        UpdateHistoryState();
        RefreshPreview();
    }

    [RelayCommand]
    private void SaveImage()
    {
        if (_canvasBitmap is null) return;
        BuildCpuPreview();
        SKBitmap? processedBitmap = _processedBitmap;
        BitmapSource? preview = PreviewImage;
        if (processedBitmap is null || preview is null) return;

        string initialFolder = !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath)
            ? Path.GetDirectoryName(ImagePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string baseName = string.IsNullOrWhiteSpace(ImagePath)
            ? "Pixora"
            : Path.GetFileNameWithoutExtension(ImagePath);
        var dialog = new SaveOptionsDialogWindow(
            preview, processedBitmap.Width, processedBitmap.Height,
            initialFolder, $"{baseName}-Edited");
        if (Application.Current?.MainWindow is Window owner)
            dialog.Owner = owner;
        if (dialog.ShowDialog() != true) return;

        string extension = dialog.SelectedFormat switch
        {
            SaveImageFormat.Jpg => ".jpg",
            SaveImageFormat.Png => ".png",
            SaveImageFormat.WebP => ".webp",
            SaveImageFormat.Bmp => ".bmp",
            SaveImageFormat.Icon => ".ico",
            _ => ".jpg"
        };
        string outputPath = Path.Combine(dialog.OutputFolder, dialog.OutputFileName + extension);
        if (File.Exists(outputPath))
        {
            MessageBoxResult answer = Application.Current?.MainWindow is Window mainWindow
                ? MessageBox.Show(mainWindow, $"Replace the existing file?\n{outputPath}", "Confirm save",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning)
                : MessageBox.Show($"Replace the existing file?\n{outputPath}", "Confirm save",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        SKBitmap? resizedBitmap = null;
        try
        {
            if (dialog.SelectedFormat == SaveImageFormat.Icon)
            {
                byte[] iconData = _imageService.EncodeIcon(
                    processedBitmap, dialog.SelectedIconSizes.Where(size => size > 0).ToArray());
                File.WriteAllBytes(outputPath, iconData);
            }
            else
            {
                SKBitmap outputBitmap = processedBitmap;
                if (dialog.OutputWidth != processedBitmap.Width ||
                    dialog.OutputHeight != processedBitmap.Height)
                {
                    resizedBitmap = _imageService.Resize(
                        processedBitmap, dialog.OutputWidth, dialog.OutputHeight);
                    outputBitmap = resizedBitmap;
                }

                SKEncodedImageFormat format = dialog.SelectedFormat switch
                {
                    SaveImageFormat.Jpg => SKEncodedImageFormat.Jpeg,
                    SaveImageFormat.WebP => SKEncodedImageFormat.Webp,
                    SaveImageFormat.Bmp => SKEncodedImageFormat.Bmp,
                    _ => SKEncodedImageFormat.Png
                };
                using var image = SKImage.FromBitmap(outputBitmap);
                using var data = image.Encode(format, dialog.Quality);
                using var output = File.Create(outputPath);
                data.SaveTo(output);
            }

            OperationError = null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                   IOException or UnauthorizedAccessException or OutOfMemoryException or
                                   NotSupportedException)
        {
            OperationError = $"Unable to save image: {ex.Message}";
        }
        finally
        {
            resizedBitmap?.Dispose();
        }
    }

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(Zoom + 0.1, 3.0);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(Zoom - 0.1, 0.2);
    [RelayCommand] private void FitToScreen() => Zoom = 1.0;

    private void DisposeBitmaps()
    {
        _canvasBitmap?.Dispose();
        _processedBitmap?.Dispose();
        _canvasBitmap = _processedBitmap = null;
    }

    public void Dispose()
    {
        ClearHistory();
        ClearAdditionalLayers();
        DisposeBitmaps();
    }

    private sealed class EditorSnapshot(PhotoLayerViewModel layer, SKBitmap bitmap,
        ImageAdjustment adjustment, int x, int y, int width, int height,
        bool isInLayerCollection, int layerIndex, PhotoLayerViewModel? selectedLayerAtCapture) : IDisposable
    {
        public PhotoLayerViewModel Layer { get; } = layer;
        public int X { get; } = x;
        public int Y { get; } = y;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public bool IsInLayerCollection { get; } = isInLayerCollection;
        public int LayerIndex { get; } = layerIndex;
        public PhotoLayerViewModel? SelectedLayerAtCapture { get; } = selectedLayerAtCapture;
        public SKBitmap Bitmap { get; private set; } = bitmap;
        public ImageAdjustment Adjustment { get; } = new()
        {
            Brightness = adjustment.Brightness,
            Contrast = adjustment.Contrast,
            Saturation = adjustment.Saturation,
            Exposure = adjustment.Exposure,
            Highlights = adjustment.Highlights,
            Shadows = adjustment.Shadows,
            Temperature = adjustment.Temperature,
            Tint = adjustment.Tint,
            Filter = adjustment.Filter
        };
        public void Dispose() => Bitmap?.Dispose();
    }
}
