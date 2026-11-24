using CommunityToolkit.Mvvm.ComponentModel;
using PixoraStudio.Core.Models;
using SkiaSharp;
using System.Windows.Media.Imaging;

namespace PixoraStudio.App.ViewModels;

public partial class PhotoLayerViewModel : ObservableObject, IDisposable
{
    public PhotoLayerViewModel(string name, SKBitmap bitmap, BitmapSource thumbnail, int x, int y)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(thumbnail);
        Name = name; Bitmap = bitmap; Thumbnail = thumbnail;
        X = x; Y = y; Width = bitmap.Width; Height = bitmap.Height;
    }

    [ObservableProperty] private string name = string.Empty;
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
    [ObservableProperty] private bool isPropertiesOpen;
    private float _opacity = 1f;
    private PhotoLayerContentType _contentType = PhotoLayerContentType.Image;
    private string _textContent = "Your text";
    private string _textAlignment = "Left";
    private string _fontFamily = "Arial";
    private float _fontSize = 64;
    private string _fillColor = "#FFFFFFFF";
    private string _strokeColor = "#FF202020";
    private float _strokeWidth;
    private string _shapeType = "Rectangle";
    private bool _isUpdatingBounds;

    public bool IsEditable => !IsLocked;

    public float Opacity
    {
        get => _opacity;
        set
        {
            var value = Math.Clamp(float.IsFinite(value) ? value : 1f, 0f, 1f);
            if (SetProperty(ref _opacity, value))
            {
                OnPropertyChanged(nameof(OpacityPercent));
                AppearanceChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public double OpacityPercent
    {
        get => _opacity * 100d;
        set
        {
            var percent = Math.Clamp(double.IsFinite(value) ? value : 100d, 0d, 100d);
            Opacity = (float)(percent / 100d);
        }
    }
    public PhotoLayerContentType ContentType
    {
        get => _contentType;
        set { if (IsLocked) return; if (SetProperty(ref _contentType, value)) ContentChanged?.Invoke(this, EventArgs.Empty); }
    }
    public string TextContent
    {
        get => _textContent;
        set { if (IsLocked) return; if (SetProperty(ref _textContent, value ?? string.Empty)) ContentChanged?.Invoke(this, EventArgs.Empty); }
    }
    public string TextAlignment
    {
        get => _textAlignment;
        set { if (IsLocked) return; if (SetProperty(ref _textAlignment, value is "Center" or "Right" ? value : "Left")) ContentChanged?.Invoke(this, EventArgs.Empty); }
    }
    public string FontFamily
    {
        get => _fontFamily;
        set { if (IsLocked) return; if (SetProperty(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Arial" : value.Trim())) ContentChanged?.Invoke(this, EventArgs.Empty); }
    }
    public float FontSize
    {
        get => _fontSize;
        set { if (IsLocked) return; if (SetProperty(ref _fontSize, Math.Clamp(float.IsFinite(value) ? value : 64f, 1f, 512f))) ContentChanged?.Invoke(this, EventArgs.Empty); }
    }
    public string FillColor
    {
        get => _fillColor;
        set { if (IsLocked) return; if (SetProperty(ref _fillColor, NormalizeColor(value, "#FFFFFFFF"))) ContentChanged?.Invoke(this, EventArgs.Empty); }
    }
    public string StrokeColor
    {
        get => _strokeColor;
        set { if (IsLocked) return; if (SetProperty(ref _strokeColor, NormalizeColor(value, "#FF202020"))) ContentChanged?.Invoke(this, EventArgs.Empty); }
    }
    public float StrokeWidth
    {
        get => _strokeWidth;
        set { if (IsLocked) return; if (SetProperty(ref _strokeWidth, Math.Clamp(float.IsFinite(value) ? value : 0f, 0f, 128f))) ContentChanged?.Invoke(this, EventArgs.Empty); }
    }
    public string ShapeType
    {
        get => _shapeType;
        set { if (IsLocked) return; if (SetProperty(ref _shapeType, value is "Ellipse" or "Line" or "Arrow" ? value : "Rectangle")) ContentChanged?.Invoke(this, EventArgs.Empty); }
    }

    public event EventHandler? VisibilityChanged;
    public event EventHandler? GeometryChanged;
    public event EventHandler? AppearanceChanged;
    public event EventHandler? ContentChanged;

    private static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { _ = SKColor.Parse(value); return value; }
        catch { return fallback; }
    }

    partial void OnIsVisibleChanged(bool value) => VisibilityChanged?.Invoke(this, EventArgs.Empty);
    partial void OnIsLockedChanged(bool value) { OnPropertyChanged(nameof(IsEditable)); AppearanceChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnXChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnYChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnWidthChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnHeightChanged(int value) { if (!_isUpdatingBounds) GeometryChanged?.Invoke(this, EventArgs.Empty); }

    public void SetBounds(int x, int y, int width, int height, bool ignoreLock = false)
    {
        if (IsLocked && !ignoreLock) return;
        _isUpdatingBounds = true;
        try { X = x; Y = y; Width = Math.Max(1, width); Height = Math.Max(1, height); }
        finally { _isUpdatingBounds = false; }
        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceBitmap(SKBitmap replacement, bool ignoreLock = false)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (IsLocked && !ignoreLock) throw new InvalidOperationException("A locked layer cannot be modified.");
        var previous = Bitmap; Bitmap = replacement; Width = replacement.Width; Height = replacement.Height;
        previous.Dispose(); GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Bitmap.Dispose();
}
