using SkiaSharp;

namespace PixoraStudio.Core.Interfaces;

public sealed record AiUpscaleProgress(string Message, double ProgressPercentage);

public interface IAiUpscalingService
{
    bool IsModelAvailable { get; }
    Task<SKBitmap> UpscaleFourTimesAsync(
        SKBitmap source,
        IProgress<AiUpscaleProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
