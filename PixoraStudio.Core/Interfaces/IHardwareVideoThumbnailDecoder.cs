using PixoraStudio.Core.Models;

namespace PixoraStudio.Core.Interfaces;

/// <summary>
/// Optional platform decoder used to generate thumbnails with hardware video decoding.
/// Returns false when the backend is unavailable; callers should then use the managed decoder.
/// </summary>
public interface IHardwareVideoThumbnailDecoder
{
    Task<bool> TryGenerateThumbnailsAsync(
        string filePath,
        IReadOnlyList<TimeSpan> positions,
        IReadOnlyList<string> outputPaths,
        IProgress<VideoThumbnailResult>? progress = null,
        CancellationToken cancellationToken = default);
}
