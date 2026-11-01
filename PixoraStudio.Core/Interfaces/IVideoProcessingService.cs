
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
    }
}
