using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Core.Interfaces;

public interface IPixelArtUpscalingService
{
    /// <summary>
    /// Upscales a bitmap using an integer pixel-art algorithm. Nearest neighbour supports
    /// factors 1-16, Scale2x supports 2/4/8/16, and Scale3x supports 3/9.
    /// </summary>
    SKBitmap Upscale(SKBitmap source, int scale, PixelArtUpscalingAlgorithm algorithm);
}
