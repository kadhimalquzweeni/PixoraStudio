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
    [Fact]
    public void GenerateThumbnailsAsync_ShouldRejectMismatchedPositionsAndPaths()
    {
        VideoProcessingService service = new();
        string filePath = Path.GetTempFileName();

        try
        {
            Assert.Throws<ArgumentException>(() =>
                service.GenerateThumbnailsAsync(
                    filePath,
                    [TimeSpan.Zero, TimeSpan.FromSeconds(1)],
                    [Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")]));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task GenerateThumbnailsAsync_ShouldRejectUnsortedPositions()
    {
        VideoProcessingService service = new();
        string filePath = Path.GetTempFileName();

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.GenerateThumbnailsAsync(
                    filePath,
                    [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)],
                    [
                        Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png"),
                        Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")
                    ]));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task GenerateThumbnailsAsync_ShouldHonorCancellationBeforeDecoding()
    {
        VideoProcessingService service = new();
        string filePath = Path.GetTempFileName();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.GenerateThumbnailsAsync(
                    filePath,
                    [TimeSpan.Zero],
                    [Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")],
                    cancellationToken: cancellation.Token));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
