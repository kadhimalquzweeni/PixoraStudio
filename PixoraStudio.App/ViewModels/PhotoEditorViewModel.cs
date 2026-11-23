using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;
using System.IO;
using System.Collections.ObjectModel;
using PixoraStudio.Core.Imaging;
using System.Windows.Media.Imaging;

namespace PixoraStudio.App.ViewModels;

public partial class PhotoEditorViewModel : ObservableObject, IDisposable
{
    private const int MaximumHistoryEntries = 50;
    private readonly IImageProcessingService _imageService;
    private readonly Stack<EditorSnapshot> _undoStack = new();
    private readonly Stack<EditorSnapshot> _redoStack = new();
    private SKBitmap? _canvasBitmap;
    private SKBitmap? _processedBitmap;
    private ImageAdjustment _lastCommittedAdjustment = new();
    private bool _isRestoringSnapshot;
    private float _resizeAspectRatio = 1f;
    private PhotoLayerViewModel? _adjustmentLayer;

    [ObservableProperty] private string? imagePath;
    [ObservableProperty] private PhotoLayerViewModel? selectedLayer;
    public ObservableCollection<PhotoLayerViewModel> AdditionalLayers { get; } = [];
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
    [ObservableProperty] private bool showOriginal;
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
    [ObservableProperty] private bool iconSize16 = true;
    [ObservableProperty] private bool iconSize24 = true;
    [ObservableProperty] private bool iconSize32 = true;
    [ObservableProperty] private bool iconSize48 = true;
    [ObservableProperty] private bool iconSize64 = true;
    [ObservableProperty] private bool iconSize128 = true;
    [ObservableProperty] private bool iconSize256 = true;

    public PhotoEditorViewModel(IImageProcessingService imageService) => _imageService = imageService;

    public void LoadImage(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ClearHistory();
        ClearAdditionalLayers();
        DisposeBitmaps();
        var firstImage = _imageService.LoadImage(filePath);
        _canvasBitmap = ImageDocumentCanvas.Create(firstImage.Width, firstImage.Height);
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

    private ImageAdjustment GetCurrentAdjustment() => new()
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

    private void SaveUndoState()
    {
        if (SelectedLayer is null) return;
        PushHistory(_undoStack, CaptureSnapshot());
        ClearStack(_redoStack);
        UpdateHistoryState();
    }

    private EditorSnapshot CaptureSnapshot() =>
        new(_imageService.CloneImage(SelectedLayer!.Bitmap), GetCurrentAdjustment());

    private void PushHistory(Stack<EditorSnapshot> stack, EditorSnapshot snapshot)
    {
        stack.Push(snapshot);
        while (stack.Count > MaximumHistoryEntries)
        {
            var retained = stack.Reverse().Skip(Math.Max(0, stack.Count - MaximumHistoryEntries)).ToArray();
            ClearStack(stack);
            foreach (var entry in retained) stack.Push(entry);
        }
    }

    private static void ClearStack(Stack<EditorSnapshot> stack)
    {
        while (stack.TryPop(out var snapshot)) snapshot.Dispose();
    }

    private void ClearHistory()
    {
        ClearStack(_undoStack);
        ClearStack(_redoStack);
        UpdateHistoryState();
    }

    private void UpdateHistoryState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        if (SelectedLayer is null) { snapshot.Dispose(); return; }
        SelectedLayer.ReplaceBitmap(_imageService.CloneImage(snapshot.Bitmap));
        SelectedLayer.Adjustment = snapshot.Adjustment;
        snapshot.DetachBitmap();
        _isRestoringSnapshot = true;
        try
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
        }
        finally { _isRestoringSnapshot = false; }
        CropX = CropY = 0;
        CropWidth = ResizeWidth = SelectedLayer.Bitmap.Width;
        CropHeight = ResizeHeight = SelectedLayer.Bitmap.Height;
        _resizeAspectRatio = (float)SelectedLayer.Bitmap.Width / Math.Max(1, SelectedLayer.Bitmap.Height);
        RefreshPreview();
        snapshot.Dispose();
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
            var entries = new List<(SKBitmap Bitmap, int X, int Y, int Width, int Height, bool IsVisible)>(AdditionalLayers.Count);
            foreach (var layer in AdditionalLayers.Reverse())
            {
                var adjustment = ShowOriginal ? new ImageAdjustment()
                    : ReferenceEquals(layer, SelectedLayer) ? GetCurrentAdjustment() : layer.Adjustment;
                var rendered = _imageService.ApplyAdjustments(layer.Bitmap, adjustment);
                renderedLayers.Add(rendered);
                entries.Add((rendered, layer.X, layer.Y, layer.Width, layer.Height, layer.IsVisible));
            }
            _processedBitmap?.Dispose();
            _processedBitmap = ImageLayerCompositor.Composite(_canvasBitmap, entries);
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
        foreach (var layer in AdditionalLayers.Reverse())
        {
            var adjustment = ShowOriginal ? new ImageAdjustment()
                : ReferenceEquals(layer, SelectedLayer) ? GetCurrentAdjustment() : layer.Adjustment;
            layers.Add(new GpuImageLayer(
                layer.Bitmap, layer.X, layer.Y, layer.Width, layer.Height,
                layer.IsVisible, adjustment));
        }

        GpuImageRenderer.Render(canvas, ImageWidth, ImageHeight, layers);
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
        CropWidth = ResizeWidth = ImageWidth;
        CropHeight = ResizeHeight = ImageHeight;
        _resizeAspectRatio = ImageHeight == 0 ? 1f : (float)ImageWidth / ImageHeight;
    }

    private void ApplyTransform(Func<SKBitmap, SKBitmap> transform)
    {
        if (SelectedLayer is null) return;
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

    partial void OnBrightnessChanged(float value) { if (IsImageLoaded) RefreshPreview(); }
    partial void OnContrastChanged(float value) { if (IsImageLoaded) RefreshPreview(); }
    partial void OnSaturationChanged(float value) { if (IsImageLoaded) RefreshPreview(); }
    partial void OnExposureChanged(float value) { if (IsImageLoaded) RefreshPreview(); }
    partial void OnHighlightsChanged(float value) { if (IsImageLoaded) RefreshPreview(); }
    partial void OnShadowsChanged(float value) { if (IsImageLoaded) RefreshPreview(); }
    partial void OnTemperatureChanged(float value) { if (IsImageLoaded) RefreshPreview(); }
    partial void OnTintChanged(float value) { if (IsImageLoaded) RefreshPreview(); }
    partial void OnSelectedFilterChanged(ImageFilter value) { if (IsImageLoaded) RefreshPreview(); }
    partial void OnShowOriginalChanged(bool value) { if (IsImageLoaded) RefreshPreview(); }

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
    private void AddImageLayer()
    {
        if (_canvasBitmap is null) return;
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
            AttachLayer(layer);
            AdditionalLayers.Insert(0, layer);
            SelectedLayer = layer;
            OperationError = null;
            RefreshPreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            OperationError = $"Unable to add image layer: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveLayer(PhotoLayerViewModel? layer)
    {
        if (layer is null || !AdditionalLayers.Remove(layer)) return;
        bool removingSelected = ReferenceEquals(SelectedLayer, layer);
        DetachLayer(layer);
        layer.Dispose();
        if (removingSelected) SelectedLayer = AdditionalLayers.FirstOrDefault();
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
    }

    private void DetachLayer(PhotoLayerViewModel layer)
    {
        layer.VisibilityChanged -= OnLayerVisibilityChanged;
        layer.GeometryChanged -= OnLayerGeometryChanged;
    }

    private void OnLayerVisibilityChanged(object? sender, EventArgs e) => RefreshPreview();

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
            var entries = new List<(SKBitmap Bitmap, int X, int Y, int Width, int Height, bool IsVisible)>(
                Math.Max(0, AdditionalLayers.Count - 1));

            foreach (var layer in AdditionalLayers.Reverse())
            {
                if (ReferenceEquals(layer, selected) || !layer.IsVisible)
                    continue;

                var adjustment = ShowOriginal ? new ImageAdjustment() : layer.Adjustment;
                var rendered = _imageService.ApplyAdjustments(layer.Bitmap, adjustment);
                renderedLayers.Add(rendered);
                entries.Add((rendered, layer.X, layer.Y, layer.Width, layer.Height, true));
            }

            using var baseComposite = ImageLayerCompositor.Composite(_canvasBitmap, entries);
            var selectedAdjustment = ShowOriginal ? new ImageAdjustment() : GetCurrentAdjustment();
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
        if (_adjustmentLayer is not null)
            _adjustmentLayer.Adjustment = GetCurrentAdjustment();
        _adjustmentLayer = value;
        foreach (var layer in AdditionalLayers) layer.IsSelected = ReferenceEquals(layer, value);
        if (value is null) return;
        if (IsImageLoaded) ClearHistory();
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
        finally { _isRestoringSnapshot = false; }
        if (IsImageLoaded) RefreshPreview();
    }

    [RelayCommand]
    private void CommitAdjustments()
    {
        if (!IsImageLoaded || SelectedLayer is null) return;
        var current = GetCurrentAdjustment();
        if (current.Brightness == _lastCommittedAdjustment.Brightness &&
            current.Contrast == _lastCommittedAdjustment.Contrast &&
            current.Saturation == _lastCommittedAdjustment.Saturation &&
            current.Exposure == _lastCommittedAdjustment.Exposure &&
            current.Highlights == _lastCommittedAdjustment.Highlights &&
            current.Shadows == _lastCommittedAdjustment.Shadows &&
            current.Temperature == _lastCommittedAdjustment.Temperature &&
            current.Tint == _lastCommittedAdjustment.Tint &&
            current.Filter == _lastCommittedAdjustment.Filter) return;

        PushHistory(_undoStack, new EditorSnapshot(
            _imageService.CloneImage(SelectedLayer.Bitmap), SelectedLayer.Adjustment));
        ClearStack(_redoStack);
        SelectedLayer.Adjustment = current;
        _lastCommittedAdjustment = current;
        UpdateHistoryState();
    }

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
        if (_undoStack.Count == 0 || SelectedLayer is null) return;
        PushHistory(_redoStack, CaptureSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        UpdateHistoryState();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0 || SelectedLayer is null) return;
        PushHistory(_undoStack, CaptureSnapshot());
        RestoreSnapshot(_redoStack.Pop());
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
        if (!IsImageLoaded || SelectedLayer is null) return;
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
        UpdateHistoryState();
        RefreshPreview();
    }

    [RelayCommand]
    private void ExportIcon()
    {
        if (_canvasBitmap is null) return;
        BuildCpuPreview();
        if (_processedBitmap is null) return;

        int[] sizes =
        [
            IconSize16 ? 16 : 0,
            IconSize24 ? 24 : 0,
            IconSize32 ? 32 : 0,
            IconSize48 ? 48 : 0,
            IconSize64 ? 64 : 0,
            IconSize128 ? 128 : 0,
            IconSize256 ? 256 : 0
        ];
        int[] selectedSizes = sizes.Where(size => size > 0).ToArray();
        if (selectedSizes.Length == 0)
        {
            OperationError = "Select at least one icon size.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Windows Icon",
            Filter = "Icon file|*.ico",
            DefaultExt = ".ico",
            AddExtension = true,
            FileName = $"{(ImagePath is null ? "Pixora-Icon" : Path.GetFileNameWithoutExtension(ImagePath))}.ico"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            byte[] icon = _imageService.EncodeIcon(_processedBitmap, selectedSizes);
            File.WriteAllBytes(dialog.FileName, icon);
            OperationError = null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            OperationError = ex.Message;
        }
    }

    [RelayCommand]
    private void SaveImage()
    {
        if (_canvasBitmap is null) return;
        BuildCpuPreview();
        if (_processedBitmap is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Image",
            Filter = "PNG Image|*.png|JPEG Image|*.jpg;*.jpeg|WebP Image|*.webp|BMP Image|*.bmp",
            FileName = "Pixora-Edited"
        };
        if (dialog.ShowDialog() != true) return;
        var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var format = extension switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            ".bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png
        };
        using var image = SKImage.FromBitmap(_processedBitmap);
        using var data = image.Encode(format, 95);
        using var output = File.Create(dialog.FileName);
        data.SaveTo(output);
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

    private sealed class EditorSnapshot(SKBitmap bitmap, ImageAdjustment adjustment) : IDisposable
    {
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
        public void DetachBitmap() => Bitmap = null!;
        public void Dispose() => Bitmap?.Dispose();
    }
}
