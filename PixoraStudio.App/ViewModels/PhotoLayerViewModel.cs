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
    }

    public string Name { get; }
    public SKBitmap Bitmap { get; }
    public BitmapSource Thumbnail { get; }
    public int X { get; }
    public int Y { get; }
    [ObservableProperty] private bool isVisible = true;
    [ObservableProperty] private bool isSelected;

    public void Dispose() => Bitmap.Dispose();
}