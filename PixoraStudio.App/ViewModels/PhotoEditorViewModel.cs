
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;
using System.Diagnostics.Contracts;
using System.IO;
using System.Windows.Media.Imaging;

namespace PixoraStudio.App.ViewModels;

public partial class PhotoEditorViewModel : ObservableObject, IDisposable
{
    private readonly IImageProcessingService _imageService;

    private SKBitmap? _originalBitmap;
    private SKBitmap? _processedBitmap;

    private readonly Stack<ImageAdjustment> _undoStack = new();
    private readonly Stack<ImageAdjustment> _redoStack = new();

    private ImageAdjustment _lastSavedAdjustment = new();

    [ObservableProperty]
    private string? imagePath;

    [ObservableProperty]
    private BitmapSource? previewImage;

    [ObservableProperty]
    private float brightness;

    [ObservableProperty]
    private float contrast = 1f;

    [ObservableProperty]
    private float saturation = 1f;

    [ObservableProperty]
    private ImageFilter selectedFilter = ImageFilter.None;

    [ObservableProperty]
    private bool showOriginal;

    [ObservableProperty]
    private double zoom = 1.0;

    [ObservableProperty]
    private bool isImageLoaded;

    [ObservableProperty]
    private bool canUndo;

    [ObservableProperty]
    private bool canRedo;

    public PhotoEditorViewModel(
        IImageProcessingService imageService)
    {
        _imageService = imageService;
    }

    public void LoadImage(string filePath)
    {
        DisposeBitmaps();

        _originalBitmap = _imageService.LoadImage(filePath);
        ImagePath = filePath;

        ResetAdjustments();
        IsImageLoaded = true;

        RefreshPreview();
    }

    private ImageAdjustment GetCurrentAdjustment()
    {
        return new ImageAdjustment
        {
            Brightness = Brightness,
            Contrast = Contrast,
            Saturation = Saturation,
            Filter = SelectedFilter
        };
    }

    private void SaveUndoState()
    {
        _undoStack.Push(GetCurrentAdjustment());
        _redoStack.Clear();

        UpdateHistoryState();
    }

    private void UpdateHistoryState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
    }

    private void RefreshPreview()
    {
        if (_originalBitmap == null)
            return;

        _processedBitmap?.Dispose();

        _processedBitmap = _imageService.ApplyAdjustments(
            _originalBitmap,
            GetCurrentAdjustment());

        var displayBitmap = ShowOriginal
            ? _originalBitmap
            : _processedBitmap;

        PreviewImage = ConvertToBitmapSource(displayBitmap);
    }

    private static BitmapSource ConvertToBitmapSource(
        SKBitmap bitmap)
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

    partial void OnBrightnessChanged(float value)
    {
        if (!IsImageLoaded)
            return;

        RefreshPreview();
    }

    partial void OnContrastChanged(float value)
    {
        if (!IsImageLoaded)
            return;

        RefreshPreview();
    }

    partial void OnSaturationChanged(float value)
    {
        if (!IsImageLoaded)
            return;

        RefreshPreview();
    }

    partial void OnSelectedFilterChanged(ImageFilter value)
    {
        if (!IsImageLoaded)
            return;

        RefreshPreview();
    }

    partial void OnShowOriginalChanged(bool value)
    {
        if (!IsImageLoaded)
            return;

        RefreshPreview();
    }

    [RelayCommand]
    private void ResetAdjustments()
    {
        Brightness = 0f;
        Contrast = 1f;
        Saturation = 1f;
        SelectedFilter = ImageFilter.None;

        _undoStack.Clear();
        _redoStack.Clear();

        UpdateHistoryState();

        if (IsImageLoaded)
            RefreshPreview();
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        _redoStack.Push(GetCurrentAdjustment());

        ApplyAdjustmentState(_undoStack.Pop());

        UpdateHistoryState();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        _undoStack.Push(GetCurrentAdjustment());

        ApplyAdjustmentState(_redoStack.Pop());

        UpdateHistoryState();
    }

    private void ApplyAdjustmentState(ImageAdjustment state)
    {
        Brightness = state.Brightness;
        Contrast = state.Contrast;
        Saturation = state.Saturation;
        SelectedFilter = state.Filter;

        RefreshPreview();
    }

    [RelayCommand]
    private void SaveImage()
    {
        if (_processedBitmap == null)
            return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Image",
            Filter = "PNG Image|*.png|JPEG Image|*.jpg|BMP Image|*.bmp",
            FileName = "Pixora-Edited"
        };

        if (dialog.ShowDialog() != true)
            return;

        SKEncodedImageFormat format =
            Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
            {
                ".jpg" => SKEncodedImageFormat.Jpeg,
                ".bmp" => SKEncodedImageFormat.Bmp,
                _ => SKEncodedImageFormat.Png
            };

        using var image = SKImage.FromBitmap(_processedBitmap);
        using var data = image.Encode(format, 95);

        using var output = File.OpenWrite(dialog.FileName);

        data.SaveTo(output);
    }

    [RelayCommand]
    private void ZoomIn()
    {
        Zoom = Math.Min(Zoom + 0.1, 3.0);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        Zoom = Math.Max(Zoom - 0.1, 0.2);
    }

    [RelayCommand]
    private void FitToScreen()
    {
        Zoom = 1.0;
    }

    private void DisposeBitmaps()
    {
        _originalBitmap?.Dispose();
        _processedBitmap?.Dispose();

        _originalBitmap = null;
        _processedBitmap = null;
    }

    public void Dispose()
    {
        DisposeBitmaps();
    }
}
