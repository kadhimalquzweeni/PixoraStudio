using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Infrastructure.Services;

public class ImageProcessingService : IImageProcessingService
{
    public SKBitmap LoadImage(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException("Unable to decode the selected image.");
    }

    public SKBitmap CloneImage(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Copy();
    }

    public SKBitmap Crop(SKBitmap source, SKRectI bounds)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (bounds.Left < 0 || bounds.Top < 0 ||
            bounds.Right > source.Width || bounds.Bottom > source.Height ||
            bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Crop bounds must be a non-empty rectangle inside the source image.");

        var result = new SKBitmap(bounds.Width, bounds.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, bounds, new SKRect(0, 0, bounds.Width, bounds.Height));
        canvas.Flush();
        return result;
    }

    public SKBitmap Rotate(SKBitmap source, int degrees) => Rotate(source, (float)degrees);

    /// <summary>Rotates around the image centre and expands the output to preserve all source pixels.</summary>
    public SKBitmap Rotate(SKBitmap source, float degrees)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!float.IsFinite(degrees))
            throw new ArgumentOutOfRangeException(nameof(degrees), "Rotation angle must be finite.");

        float normalized = degrees % 360f;
        if (Math.Abs(normalized) < 0.0001f || Math.Abs(Math.Abs(normalized) - 360f) < 0.0001f)
            return CloneImage(source);

        double radians = normalized * Math.PI / 180d;
        int width = Math.Max(1, (int)Math.Ceiling(
            Math.Abs(source.Width * Math.Cos(radians)) + Math.Abs(source.Height * Math.Sin(radians))));
        int height = Math.Max(1, (int)Math.Ceiling(
            Math.Abs(source.Width * Math.Sin(radians)) + Math.Abs(source.Height * Math.Cos(radians))));

        var result = new SKBitmap(width, height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(normalized);
        canvas.DrawBitmap(source, -source.Width / 2f, -source.Height / 2f,
            new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High });
        canvas.Flush();
        return result;
    }

    public SKBitmap Flip(SKBitmap source, bool horizontal, bool vertical)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!horizontal && !vertical)
            return CloneImage(source);

        var result = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(horizontal ? source.Width : 0, vertical ? source.Height : 0);
        canvas.Scale(horizontal ? -1 : 1, vertical ? -1 : 1);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return result;
    }

    public SKBitmap Resize(SKBitmap source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be greater than zero.");

        var result = new SKBitmap(width, height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();
        return result;
    }

    public SKBitmap ApplyAdjustments(SKBitmap original, ImageAdjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(adjustment);

        var result = new SKBitmap(original.Width, original.Height, original.ColorType, original.AlphaType);
        float brightness = adjustment.Brightness;
        float contrast = adjustment.Contrast;
        float saturation = adjustment.Saturation;

        for (int y = 0; y < original.Height; y++)
        for (int x = 0; x < original.Width; x++)
        {
            SKColor pixel = original.GetPixel(x, y);
            float r = pixel.Red + brightness;
            float g = pixel.Green + brightness;
            float b = pixel.Blue + brightness;

            r = ((r - 128f) * contrast) + 128f;
            g = ((g - 128f) * contrast) + 128f;
            b = ((b - 128f) * contrast) + 128f;

            float gray = (0.299f * r) + (0.587f * g) + (0.114f * b);
            r = gray + saturation * (r - gray);
            g = gray + saturation * (g - gray);
            b = gray + saturation * (b - gray);

            switch (adjustment.Filter)
            {
                case ImageFilter.Grayscale:
                    r = g = b = gray;
                    break;
                case ImageFilter.Sepia:
                    float sr = (r * 0.393f) + (g * 0.769f) + (b * 0.189f);
                    float sg = (r * 0.349f) + (g * 0.686f) + (b * 0.168f);
                    float sb = (r * 0.272f) + (g * 0.534f) + (b * 0.131f);
                    r = sr; g = sg; b = sb;
                    break;
            }

            result.SetPixel(x, y, new SKColor(Clamp(r), Clamp(g), Clamp(b), pixel.Alpha));
        }
        return result;
    }

    private static byte Clamp(float value) => (byte)Math.Clamp(value, 0f, 255f);
}
