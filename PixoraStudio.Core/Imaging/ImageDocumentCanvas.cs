using SkiaSharp;

namespace PixoraStudio.Core.Imaging;

/// <summary>
/// Creates the independent, transparent pixel surface for an image document.
/// Image content belongs to layers; the canvas does not own or contain the
/// first image opened by the user.
/// </summary>
public static class ImageDocumentCanvas
{
    public static SKBitmap Create(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        var canvas = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        canvas.Erase(SKColors.Transparent);
        return canvas;
    }
}
