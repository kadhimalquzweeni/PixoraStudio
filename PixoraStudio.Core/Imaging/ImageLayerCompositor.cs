using SkiaSharp;

namespace PixoraStudio.Core.Imaging;

/// <summary>Composites visible image layers over a fixed-size document canvas.</summary>
public static class ImageLayerCompositor
{
    public static SKBitmap Composite(
        SKBitmap background,
        IReadOnlyCollection<(SKBitmap Bitmap, int X, int Y, bool IsVisible)> layers)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(layers);
        if (background.Width <= 0 || background.Height <= 0)
            throw new ArgumentException("The document background must have non-zero dimensions.", nameof(background));

        var result = new SKBitmap(background.Width, background.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(background, 0, 0);
        foreach (var layer in layers)
        {
            ArgumentNullException.ThrowIfNull(layer.Bitmap);
            if (!layer.IsVisible) continue;
            canvas.DrawBitmap(layer.Bitmap, layer.X, layer.Y);
        }
        canvas.Flush();
        return result;
    }
}