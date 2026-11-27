using SkiaSharp;

namespace PixoraStudio.Core.Interfaces;

public sealed record AiUpscaleProgress(string Message, double ProgressPercentage);

/// <summary>Provides Pixora's optional photo-focused 4x AI upscaler.</summary>
public interface IAiUpscalingService
{
    bool IsModelAvailable();
    Task<SKBitmap> UpscaleFourTimesAsync(
        SKBitmap source,
        IProgress<AiUpscaleProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
