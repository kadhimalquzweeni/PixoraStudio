using SkiaSharp;

namespace PixoraStudio.Core.Interfaces;

public sealed record AiUpscaleProgress(string Message, double ProgressPercentage);

public enum AiUpscaleModel
{
    Photo,
    Sprite
}

public interface IAiUpscalingService
{
    bool IsModelAvailable(AiUpscaleModel model);
    Task<SKBitmap> UpscaleFourTimesAsync(
        SKBitmap source,
        AiUpscaleModel model,
        IProgress<AiUpscaleProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
