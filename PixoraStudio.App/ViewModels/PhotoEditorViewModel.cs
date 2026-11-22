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
    [ObservableProperty] private string? operationError;

    public PhotoEditorViewModel(IImageProcessingService imageService) => _imageService = imageService;

    public void LoadImage(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ClearHistory();
        DisposeBitmaps();
        _originalBitmap = _imageService.LoadImage(filePath);
        ImagePath = filePath;
        IsImageLoaded = true;
        ResetAdjustments();
        UpdateDimensions();
        RefreshPreview();
    }

    private ImageAdjustment GetCurrentAdjustment() => new()
    {
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
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
    [RelayCommand] private void FlipHorizontal() => ApplyTransform(bitmap => _imageService.Flip(bitmap, true, false));
    [RelayCommand] private void FlipVertical() => ApplyTransform(bitmap => _imageService.Flip(bitmap, false, true));

    [RelayCommand]
    private void Crop()
    {
        if (_originalBitmap is null) return;
        ApplyTransform(bitmap => _imageService.Crop(bitmap,
            new SKRectI(CropX, CropY, checked(CropX + CropWidth), checked(CropY + CropHeight))));
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

    [RelayCommand]
    private void ResetAdjustments()
    {
        Brightness = 0f;
        Contrast = 1f;
        Saturation = 1f;
        SelectedFilter = ImageFilter.None;
        _lastCommittedAdjustment = GetCurrentAdjustment();
        if (IsImageLoaded) RefreshPreview();
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
            Filter = adjustment.Filter
        };
        public void DetachBitmap() => Bitmap = null!;
        public void Dispose() => Bitmap?.Dispose();
    }
}
