using PixoraStudio.Infrastructure.Services;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class VideoProcessingServiceTests
{
    [Fact]
    public async Task GetVideoInfoAsync_ShouldRejectMissingFileWithoutExternalTools()
    {
        VideoProcessingService service = new();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.GetVideoInfoAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4")));
    }

    [Fact]
    public async Task GenerateThumbnailAsync_ShouldRejectMissingFileWithoutExternalTools()
    {
        VideoProcessingService service = new();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.GenerateThumbnailAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4"),
                TimeSpan.Zero,
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")));
    }

    [Fact]
    public async Task GenerateThumbnailAsync_ShouldRejectNegativePosition()
    {
        string filePath = Path.GetTempFileName();
        try
        {
            VideoProcessingService service = new();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => service.GenerateThumbnailAsync(
                    filePath,
                    TimeSpan.FromSeconds(-1),
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
