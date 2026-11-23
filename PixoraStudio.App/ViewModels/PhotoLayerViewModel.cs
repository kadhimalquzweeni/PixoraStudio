using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using System.Windows.Media.Imaging;

namespace PixoraStudio.App.ViewModels;

public partial class PhotoLayerViewModel : ObservableObject, IDisposable
{
    public PhotoLayerViewModel(string name, SKBitmap bitmap, BitmapSource thumbnail, int x, int y)
    {
        Name = name;
        Bitmap = bitmap;
        Thumbnail = thumbnail;
        X = x;
        Y = y;
        Width = bitmap.Width;
        Height = bitmap.Height;
    }

    public string Name { get; }
    public SKBitmap Bitmap { get; }
    public BitmapSource Thumbnail { get; }
    [ObservableProperty] private int x;
    [ObservableProperty] private int y;
    [ObservableProperty] private int width;
    [ObservableProperty] private int height;
    [ObservableProperty] private bool isVisible = true;
    [ObservableProperty] private bool isSelected;

    public event EventHandler? VisibilityChanged;
    public event EventHandler? GeometryChanged;
    private bool _isUpdatingBounds;

    partial void OnIsVisibleChanged(bool value) => VisibilityChanged?.Invoke(this, EventArgs.Empty);
    partial void OnXChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnYChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnWidthChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnHeightChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }

    public void SetBounds(int x, int y, int width, int height)
    {
        _isUpdatingBounds = true;
        try
        {
            X = x;
            Y = y;
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
        }
        finally { _isUpdatingBounds = false; }
        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Bitmap.Dispose();
}
