using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;
using System.IO;
using System.Windows.Media.Imaging;

namespace PixoraStudio.App.ViewModels;

public partial class PhotoEditorViewModel : ObservableObject, IDisposable
{
    private const int MaximumHistoryEntries = 50;
    private readonly IImageProcessingService _imageService;
    private readonly Stack<EditorSnapshot> _undoStack = new();
    private readonly Stack<EditorSnapshot> _redoStack = new();
    private SKBitmap? _originalBitmap;
    private SKBitmap? _processedBitmap;
    private ImageAdjustment _lastCommittedAdjustment = new();
    private bool _isRestoringSnapshot;
    private float _resizeAspectRatio = 1f;

    [ObservableProperty] private string? imagePath;
    [ObservableProperty] private BitmapSource? previewImage;
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
        DisposeBitmaps();
        _originalBitmap = _imageService.LoadImage(filePath);
        ImagePath = filePath;
        IsImageLoaded = false;
        SetDefaultAdjustments();
        UpdateDimensions();
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
        if (_originalBitmap is null) return;
        PushHistory(_undoStack, CaptureSnapshot());
        ClearStack(_redoStack);
        UpdateHistoryState();
    }

    private EditorSnapshot CaptureSnapshot() =>
        new(_imageService.CloneImage(_originalBitmap!), GetCurrentAdjustment());

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
        _originalBitmap?.Dispose();
        _originalBitmap = snapshot.Bitmap;
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
        UpdateDimensions();
        RefreshPreview();
        snapshot.Dispose();
    }

    private void RefreshPreview()
    {
        if (_originalBitmap is null) return;
        _processedBitmap?.Dispose();
        _processedBitmap = _imageService.ApplyAdjustments(_originalBitmap, GetCurrentAdjustment());
        PreviewImage = ConvertToBitmapSource(ShowOriginal ? _originalBitmap : _processedBitmap);
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
        if (_originalBitmap is null) return;
        ImageWidth = _originalBitmap.Width;
        ImageHeight = _originalBitmap.Height;
        CropX = CropY = 0;
        CropWidth = ResizeWidth = ImageWidth;
        CropHeight = ResizeHeight = ImageHeight;
        _resizeAspectRatio = ImageHeight == 0 ? 1f : (float)ImageWidth / ImageHeight;
    }

    private void ApplyTransform(Func<SKBitmap, SKBitmap> transform)
    {
        if (_originalBitmap is null) return;
        try
        {
            using var transformed = transform(_originalBitmap);
            SaveUndoState();
            _lastCommittedAdjustment = GetCurrentAdjustment();
            var replacement = _imageService.CloneImage(transformed);
            _originalBitmap.Dispose();
            _originalBitmap = replacement;
            OperationError = null;
            UpdateDimensions();
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
    private void CommitAdjustments()
    {
        if (!IsImageLoaded) return;
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
            _imageService.CloneImage(_originalBitmap!), _lastCommittedAdjustment));
        ClearStack(_redoStack);
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
        if (_originalBitmap is null) return;
        ApplyTransform(bitmap => _imageService.Crop(bitmap,
            new SKRectI(CropX, CropY, checked(CropX + CropWidth), checked(CropY + CropHeight))));
        IsCropMode = false;
    }

    [RelayCommand]
    private void Resize()
    {
        if (_originalBitmap is null) return;
        ApplyTransform(bitmap => _imageService.Resize(bitmap, ResizeWidth, ResizeHeight));
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0 || _originalBitmap is null) return;
        PushHistory(_redoStack, CaptureSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        UpdateHistoryState();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0 || _originalBitmap is null) return;
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
        if (!IsImageLoaded) return;
        var current = GetCurrentAdjustment();
        if (current.Brightness == 0f && current.Contrast == 1f &&
            current.Saturation == 1f && current.Exposure == 0f &&
            current.Highlights == 0f && current.Shadows == 0f &&
            current.Temperature == 0f && current.Tint == 0f &&
            current.Filter == ImageFilter.None) return;

        SaveUndoState();
        SetDefaultAdjustments();
        ClearStack(_redoStack);
        UpdateHistoryState();
        RefreshPreview();
    }

    [RelayCommand]
    private void ExportIcon()
    {
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
        _originalBitmap?.Dispose();
        _processedBitmap?.Dispose();
        _originalBitmap = _processedBitmap = null;
    }

    public void Dispose()
    {
        ClearHistory();
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
