
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
            ?? throw new InvalidOperationException(
                "Unable to decode the selected image.");
    }

    public SKBitmap CloneImage(SKBitmap source)
    {
        return source.Copy();
    }

    public SKBitmap ApplyAdjustments(
        SKBitmap original,
        ImageAdjustment adjustment)
    {
        var result = new SKBitmap(
            original.Width,
            original.Height,
            original.ColorType,
            original.AlphaType);

        float brightness = adjustment.Brightness;
        float contrast = adjustment.Contrast;
        float saturation = adjustment.Saturation;

        for (int y = 0; y < original.Height; y++)
        {
            for (int x = 0; x < original.Width; x++)
            {
                SKColor pixel = original.GetPixel(x, y);

                float r = pixel.Red;
                float g = pixel.Green;
                float b = pixel.Blue;

                // Brightness
                r += brightness;
                g += brightness;
                b += brightness;

                // Contrast
                r = ((r - 128f) * contrast) + 128f;
                g = ((g - 128f) * contrast) + 128f;
                b = ((b - 128f) * contrast) + 128f;

                // Saturation
                float gray = (0.299f * r)
                           + (0.587f * g)
                           + (0.114f * b);

                r = gray + saturation * (r - gray);
                g = gray + saturation * (g - gray);
                b = gray + saturation * (b - gray);

                // Filters
                switch (adjustment.Filter)
                {
                    case ImageFilter.Grayscale:
                        r = gray;
                        g = gray;
                        b = gray;
                        break;

                    case ImageFilter.Sepia:
                        float sepiaR = (r * 0.393f)
                                      + (g * 0.769f)
                                      + (b * 0.189f);

                        float sepiaG = (r * 0.349f)
                                      + (g * 0.686f)
                                      + (b * 0.168f);

                        float sepiaB = (r * 0.272f)
                                      + (g * 0.534f)
                                      + (b * 0.131f);

                        r = sepiaR;
                        g = sepiaG;
                        b = sepiaB;
                        break;
                }

                result.SetPixel(
                    x,
                    y,
                    new SKColor(
                        Clamp(r),
                        Clamp(g),
                        Clamp(b),
                        pixel.Alpha));
            }
        }

        return result;
    }

    private static byte Clamp(float value)
    {
        return (byte)Math.Clamp(value, 0f, 255f);
    }
}
