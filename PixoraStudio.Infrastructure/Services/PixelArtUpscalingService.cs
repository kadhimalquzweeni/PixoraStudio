using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Integer-only pixel-art upscaling. All algorithms copy source colours directly and
/// preserve alpha; no filtering, interpolation, or colour blending is performed.
/// </summary>
public sealed class PixelArtUpscalingService : IPixelArtUpscalingService
{
    public const int MaximumScale = 16;
    public const int MaximumDimension = 16384;

    public SKBitmap Upscale(SKBitmap source, int scale, PixelArtUpscalingAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width <= 0 || source.Height <= 0)
            throw new ArgumentException("The source image must have non-zero dimensions.", nameof(source));
        if (scale < 1 || scale > MaximumScale)
            throw new ArgumentOutOfRangeException(nameof(scale), $"Scale must be between 1 and {MaximumScale}.");
        if (source.Width > MaximumDimension / scale || source.Height > MaximumDimension / scale)
            throw new ArgumentOutOfRangeException(nameof(scale),
                $"The upscaled image cannot exceed {MaximumDimension} pixels in either dimension.");

        return algorithm switch
        {
            PixelArtUpscalingAlgorithm.NearestNeighbour => NearestNeighbour(source, scale),
            PixelArtUpscalingAlgorithm.Scale2x => Scale2xRepeated(source, scale),
            PixelArtUpscalingAlgorithm.Scale3x => Scale3xRepeated(source, scale),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown pixel-art algorithm.")
        };
    }

    private static SKBitmap NearestNeighbour(SKBitmap source, int scale)
    {
        var result = CreateBitmap(source.Width * scale, source.Height * scale);
        for (int y = 0; y < result.Height; y++)
        for (int x = 0; x < result.Width; x++)
            result.SetPixel(x, y, source.GetPixel(x / scale, y / scale));
        return result;
    }

    private static SKBitmap Scale2xRepeated(SKBitmap source, int scale)
    {
        if (scale is not (2 or 4 or 8 or 16))
            throw new ArgumentOutOfRangeException(nameof(scale),
                "Scale2x supports 2x, 4x, 8x and 16x output.");

        SKBitmap current = source.Copy();
        try
        {
            for (int pass = 0; pass < BitOperationsLog2(scale); pass++)
            {
                SKBitmap next = Scale2xPass(current);
                current.Dispose();
                current = next;
            }

            return current.Copy();
        }
        finally
        {
            current.Dispose();
        }
    }

    private static SKBitmap Scale3xRepeated(SKBitmap source, int scale)
    {
        if (scale is not (3 or 9))
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale3x supports 3x and 9x output.");

        SKBitmap current = source.Copy();
        try
        {
            int passes = scale == 9 ? 2 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                SKBitmap next = Scale3xPass(current);
                current.Dispose();
                current = next;
            }

            return current.Copy();
        }
        finally
        {
            current.Dispose();
        }
    }

    private static int BitOperationsLog2(int value) => value switch
    {
        2 => 1,
        4 => 2,
        8 => 3,
        16 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static SKBitmap Scale2xPass(SKBitmap source)
    {
        var result = CreateBitmap(checked(source.Width * 2), checked(source.Height * 2));
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            SKColor e = source.GetPixel(x, y);
            SKColor b = source.GetPixel(x, Math.Max(0, y - 1));
            SKColor d = source.GetPixel(Math.Max(0, x - 1), y);
            SKColor f = source.GetPixel(Math.Min(source.Width - 1, x + 1), y);
            SKColor h = source.GetPixel(x, Math.Min(source.Height - 1, y + 1));

            SKColor e0 = e, e1 = e, e2 = e, e3 = e;
            if (b != h && d != f)
            {
                if (d == b) e0 = d;
                if (b == f) e1 = f;
                if (d == h) e2 = d;
                if (h == f) e3 = f;
            }

            result.SetPixel(x * 2, y * 2, e0);
            result.SetPixel(x * 2 + 1, y * 2, e1);
            result.SetPixel(x * 2, y * 2 + 1, e2);
            result.SetPixel(x * 2 + 1, y * 2 + 1, e3);
        }
        return result;
    }

    private static SKBitmap Scale3xPass(SKBitmap source)
    {
        var result = CreateBitmap(checked(source.Width * 3), checked(source.Height * 3));
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            SKColor a = source.GetPixel(Math.Max(0, x - 1), Math.Max(0, y - 1));
            SKColor b = source.GetPixel(x, Math.Max(0, y - 1));
            SKColor c = source.GetPixel(Math.Min(source.Width - 1, x + 1), Math.Max(0, y - 1));
            SKColor d = source.GetPixel(Math.Max(0, x - 1), y);
            SKColor e = source.GetPixel(x, y);
            SKColor f = source.GetPixel(Math.Min(source.Width - 1, x + 1), y);
            SKColor g = source.GetPixel(Math.Max(0, x - 1), Math.Min(source.Height - 1, y + 1));
            SKColor h = source.GetPixel(x, Math.Min(source.Height - 1, y + 1));
            SKColor i = source.GetPixel(Math.Min(source.Width - 1, x + 1), Math.Min(source.Height - 1, y + 1));

            SKColor p0 = e, p1 = e, p2 = e, p3 = e, p4 = e;
            SKColor p5 = e, p6 = e, p7 = e, p8 = e;
            if (b != h && d != f)
            {
                if (d == b) p0 = d;
                if ((d == b && e != c) || (b == f && e != a)) p1 = b;
                if (b == f) p2 = f;
                if ((d == b && e != g) || (d == h && e != a)) p3 = d;
                if ((b == f && e != i) || (h == f && e != c)) p5 = f;
                if (d == h) p6 = d;
                if ((d == h && e != i) || (h == f && e != g)) p7 = h;
                if (h == f) p8 = f;
            }

            int ox = x * 3, oy = y * 3;
            result.SetPixel(ox, oy, p0);
            result.SetPixel(ox + 1, oy, p1);
            result.SetPixel(ox + 2, oy, p2);
            result.SetPixel(ox, oy + 1, p3);
            result.SetPixel(ox + 1, oy + 1, p4);
            result.SetPixel(ox + 2, oy + 1, p5);
            result.SetPixel(ox, oy + 2, p6);
            result.SetPixel(ox + 1, oy + 2, p7);
            result.SetPixel(ox + 2, oy + 2, p8);
        }
        return result;
    }

    private static SKBitmap CreateBitmap(int width, int height) =>
        new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
}
