using PixoraStudio.Infrastructure.Services;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;

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
    public async Task GenerateThumbnailsAsync_ShouldRejectMismatchedPositionsAndPaths()
    {
        VideoProcessingService service = new();
        string filePath = Path.GetTempFileName();

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
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

    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(1, 8, 1)]
    [InlineData(2, 8, 2)]
    [InlineData(12, 1, 1)]
    [InlineData(12, 2, 2)]
    [InlineData(12, 4, 4)]
    [InlineData(12, 16, 4)]
    public void GetThumbnailWorkerCount_ShouldBoundParallelism(
        int thumbnailCount,
        int processorCount,
        int expected)
    {
        Assert.Equal(
            expected,
            VideoProcessingService.GetThumbnailWorkerCount(
                thumbnailCount, processorCount));
    }

    [Theory]
    [InlineData(12, 8, 1, 1)]
    [InlineData(12, 8, 2, 2)]
    [InlineData(12, 8, 3, 3)]
    [InlineData(12, 8, 4, 4)]
    [InlineData(12, 8, 8, 4)]
    [InlineData(2, 8, 4, 2)]
    public void GetThumbnailWorkerCount_ShouldRespectRequestedParallelism(
        int thumbnailCount,
        int processorCount,
        int requestedWorkerCount,
        int expected)
    {
        Assert.Equal(
            expected,
            VideoProcessingService.GetThumbnailWorkerCount(
                thumbnailCount, processorCount, requestedWorkerCount));
    }

    [Fact]
    public void GetThumbnailWorkerCount_ShouldRejectNonPositiveRequestedParallelism()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VideoProcessingService.GetThumbnailWorkerCount(4, 8, 0));
    }

    [Fact]
    public void GetThumbnailWorkerCount_ShouldRejectInvalidProcessorCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VideoProcessingService.GetThumbnailWorkerCount(4, 0));
    }

    [Fact]
    public void GroupBySeekSample_ShouldGroupConsecutiveThumbnailsSharingAKeyframe()
    {
        var groups = VideoProcessingService.GroupBySeekSample(
            new uint[] { 1, 1, 1, 90, 90, 300 });

        Assert.Equal(new uint[] { 1, 90, 300 }, groups.Select(group => group.FirstSample));
        Assert.Equal(new[] { 0, 1, 2 }, groups[0].Indices);
        Assert.Equal(new[] { 3, 4 }, groups[1].Indices);
        Assert.Equal(new[] { 5 }, groups[2].Indices);
    }

    [Fact]
    public void GroupBySeekSample_ShouldNotMergeNonAdjacentEqualSamples()
    {
        var groups = VideoProcessingService.GroupBySeekSample(new uint[] { 5, 9, 5 });

        Assert.Equal(3, groups.Count);
        Assert.Equal(new uint[] { 5, 9, 5 }, groups.Select(group => group.FirstSample));
    }

    [Fact]
    public void GroupBySeekSample_ShouldHandleEmptyAndSingleInput()
    {
        Assert.Empty(VideoProcessingService.GroupBySeekSample(Array.Empty<uint>()));

        var single = VideoProcessingService.GroupBySeekSample(new uint[] { 42 });
        Assert.Single(single);
        Assert.Equal(42u, single[0].FirstSample);
        Assert.Equal(new[] { 0 }, single[0].Indices);
    }
    [Fact]
    public async Task GenerateThumbnailsAsync_ShouldUseHardwareDecoderWhenItCompletesTheBatch()
    {
        string filePath = Path.GetTempFileName();
        string outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        StubHardwareVideoThumbnailDecoder hardware = new() { Result = true };

        try
        {
            VideoProcessingService service = new(hardware);
            await service.GenerateThumbnailsAsync(
                filePath,
                [TimeSpan.Zero],
                [outputPath]);

            Assert.Equal(1, hardware.CallCount);
        }
        finally
        {
            File.Delete(filePath);
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task GenerateThumbnailsAsync_ShouldFallBackToManagedDecoderWhenHardwareIsUnavailable()
    {
        string filePath = Path.GetTempFileName();
        StubHardwareVideoThumbnailDecoder hardware = new() { Result = false };

        try
        {
            VideoProcessingService service = new(hardware);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.GenerateThumbnailsAsync(
                    filePath,
                    [TimeSpan.Zero],
                    [Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")]));

            Assert.Equal(1, hardware.CallCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task GenerateThumbnailsAsync_ShouldFallBackToManagedDecoderWhenHardwareThrows()
    {
        string filePath = Path.GetTempFileName();
        StubHardwareVideoThumbnailDecoder hardware = new()
        {
            Exception = new InvalidOperationException("GPU decoder unavailable.")
        };

        try
        {
            VideoProcessingService service = new(hardware);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.GenerateThumbnailsAsync(
                    filePath,
                    [TimeSpan.Zero],
                    [Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")]));

            Assert.Equal(1, hardware.CallCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private sealed class StubHardwareVideoThumbnailDecoder : IHardwareVideoThumbnailDecoder
    {
        public bool Result { get; init; }
        public Exception? Exception { get; init; }
        public int CallCount { get; private set; }

        public Task<bool> TryGenerateThumbnailsAsync(
            string filePath,
            IReadOnlyList<TimeSpan> positions,
            IReadOnlyList<string> outputPaths,
            IProgress<VideoThumbnailResult>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Exception is not null)
                throw Exception;

            return Task.FromResult(Result);
        }
    }

}
