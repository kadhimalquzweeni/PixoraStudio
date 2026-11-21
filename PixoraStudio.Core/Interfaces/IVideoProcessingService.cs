using PixoraStudio.Core.Models;

namespace PixoraStudio.Core.Interfaces
{
    public interface IVideoProcessingService
    {
        Task<VideoInfo> GetVideoInfoAsync(string filePath);

        Task<string> GenerateThumbnailAsync(
            string filePath,
            TimeSpan position,
            string outputPath);

        Task GenerateThumbnailsAsync(
            string filePath,
            IReadOnlyList<TimeSpan> positions,
            IReadOnlyList<string> outputPaths,
            IProgress<VideoThumbnailResult>? progress = null,
            CancellationToken cancellationToken = default,
            VideoThumbnailMode mode = VideoThumbnailMode.Exact);
    }
}
