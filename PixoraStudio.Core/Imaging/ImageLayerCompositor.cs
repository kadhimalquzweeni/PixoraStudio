using SkiaSharp;

namespace PixoraStudio.Core.Imaging;

/// <summary>Composites visible image layers over a fixed-size transparent document canvas.</summary>
public static class ImageLayerCompositor
{
    public static SKBitmap Composite(
        SKBitmap background,
        IReadOnlyCollection<(SKBitmap Bitmap, int X, int Y, int Width, int Height, bool IsVisible)> layers)
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
            if (!layer.IsVisible || layer.Width <= 0 || layer.Height <= 0) continue;
            var destination = new SKRect(layer.X, layer.Y, layer.X + layer.Width, layer.Y + layer.Height);
            canvas.DrawBitmap(layer.Bitmap, destination, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        canvas.Flush();
        return result;
    }
}
