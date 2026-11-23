using SkiaSharp;

namespace PixoraStudio.Core.Imaging;

/// <summary>
/// Creates and resizes the independent pixel surface for an image document.
/// Image content belongs to layers; the canvas stores only its background.
/// </summary>
public static class ImageDocumentCanvas
{
    public static SKBitmap Create(int width, int height) =>
        Create(width, height, SKColors.Transparent);

    public static SKBitmap Create(int width, int height, SKColor backgroundColor)
    {
        ValidateDimensions(width, height);

        var canvas = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        canvas.Erase(backgroundColor);
        return canvas;
    }

    /// <summary>
    /// Resizes the document background and copies its existing pixels at the
    /// requested offset. Negative offsets crop content; positive offsets add
    /// space before the existing content.
    /// </summary>
    public static SKBitmap Resize(
        SKBitmap source,
        int width,
        int height,
        int offsetX,
        int offsetY,
        SKColor backgroundColor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateDimensions(width, height);

        var resized = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(resized);
        canvas.Clear(backgroundColor);
        canvas.DrawBitmap(source, offsetX, offsetY);
        canvas.Flush();
        return resized;
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas width must be greater than zero.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Canvas height must be greater than zero.");
    }
}
