using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using PixoraStudio.Core.Models;
using System.Windows.Media.Imaging;

namespace PixoraStudio.App.ViewModels;

public partial class PhotoLayerViewModel : ObservableObject, IDisposable
{
    public PhotoLayerViewModel(string name, SKBitmap bitmap, BitmapSource thumbnail, int x, int y)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A layer name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(thumbnail);

        Name = name;
        Bitmap = bitmap;
        Thumbnail = thumbnail;
        X = x;
        Y = y;
        Width = bitmap.Width;
        Height = bitmap.Height;
    }

    [ObservableProperty] private string name;
    public SKBitmap Bitmap { get; private set; }
    public ImageAdjustment Adjustment { get; set; } = new();
    public BitmapSource Thumbnail { get; }
    [ObservableProperty] private int x;
    [ObservableProperty] private int y;
    [ObservableProperty] private int width;
    [ObservableProperty] private int height;
    [ObservableProperty] private bool isVisible = true;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isLocked;
    [ObservableProperty] private float opacity = 1f;

    public event EventHandler? VisibilityChanged;
    public event EventHandler? GeometryChanged;
    public event EventHandler? AppearanceChanged;
    private bool _isUpdatingBounds;

    partial void OnIsVisibleChanged(bool value) => VisibilityChanged?.Invoke(this, EventArgs.Empty);
    partial void OnOpacityChanged(float value)
    {
        Opacity = Math.Clamp(float.IsFinite(value) ? value : 1f, 0f, 1f);
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }
    partial void OnIsLockedChanged(bool value) => AppearanceChanged?.Invoke(this, EventArgs.Empty);
    partial void OnXChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnYChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnWidthChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnHeightChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }

    public void SetBounds(int x, int y, int width, int height)
    {
        if (IsLocked)
            return;

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

    public void ReplaceBitmap(SKBitmap replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (IsLocked)
            throw new InvalidOperationException("A locked layer cannot be modified.");

        var previous = Bitmap;
        Bitmap = replacement;
        Width = replacement.Width;
        Height = replacement.Height;
        previous.Dispose();
        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Bitmap.Dispose();
}
