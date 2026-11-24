using SkiaSharp;

namespace PixoraStudio.Core.Interfaces;

public sealed record AiUpscaleProgress(string Message, double Percentage);

public static class AiUpscalingLimits
{
    public const int Scale = 4;
    public const int MaximumInputDimension = 4096;
    public const long MaximumOutputPixels = 48_000_000;
}

public interface IAiUpscalingService
{
    /// <summary>Upscales an image four times while preserving its alpha channel.</summary>
    Task<SKBitmap> UpscaleFourTimesAsync(
        SKBitmap source,
        IProgress<AiUpscaleProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
