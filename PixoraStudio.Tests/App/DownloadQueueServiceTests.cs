using PixoraStudio.App.Services;

namespace PixoraStudio.Tests.App;

public sealed class DownloadQueueServiceTests
{
    [Fact]
    public async Task StartAsync_ProcessesQueuedDownloadsSequentially()
    {
        var downloader = new FakeDownloadService();
        var queue = new DownloadQueueService(downloader);
        var first = queue.Add(new Uri("https://media.example/first.mp4"), "downloads");
        var second = queue.Add(new Uri("https://media.example/second.webm"), "downloads");

        await queue.StartAsync();

        Assert.Equal(2, downloader.DownloadedSources.Count);
        Assert.Equal(first.Source, downloader.DownloadedSources[0]);
        Assert.Equal(second.Source, downloader.DownloadedSources[1]);
        Assert.Equal(DownloadQueueItemStatus.Completed, first.Status);
        Assert.Equal(DownloadQueueItemStatus.Completed, second.Status);
        Assert.Equal(100d, second.Progress);
        Assert.False(queue.IsProcessing);
    }

    [Fact]
    public async Task Add_DoesNotDuplicateQueuedOrActiveSource()
    {
        var downloader = new FakeDownloadService();
        var queue = new DownloadQueueService(downloader);
        var source = new Uri("https://media.example/video.mp4");

        var first = queue.Add(source, "downloads");
        var duplicate = queue.Add(source, "downloads");

        Assert.Same(first, duplicate);
        Assert.Single(queue.Items);

        await queue.StartAsync();
    }

    [Fact]
    public async Task StartAsync_ContinuesAfterFailureAndAllowsRetry()
    {
        var downloader = new FakeDownloadService { FailFirstAttempt = true };
        var queue = new DownloadQueueService(downloader);
        var item = queue.Add(new Uri("https://media.example/video.mp4"), "downloads");

        await queue.StartAsync();

        Assert.Equal(DownloadQueueItemStatus.Failed, item.Status);
        Assert.Equal("Simulated failure.", item.ErrorMessage);

        Assert.True(queue.Retry(item));
        await queue.StartAsync();

        Assert.Equal(DownloadQueueItemStatus.Completed, item.Status);
        Assert.Equal(2, downloader.DownloadedSources.Count);
    }

    [Fact]
    public async Task Cancel_CancelsQueuedItemWithoutStartingIt()
    {
        var downloader = new FakeDownloadService();
        var queue = new DownloadQueueService(downloader);
        var item = queue.Add(new Uri("https://media.example/video.mp4"), "downloads");

        queue.Cancel(item);
        await queue.StartAsync();

        Assert.Equal(DownloadQueueItemStatus.Cancelled, item.Status);
        Assert.Empty(downloader.DownloadedSources);
    }

    private sealed class FakeDownloadService : IDirectMediaDownloadService
    {
        public List<Uri> DownloadedSources { get; } = [];
        public bool FailFirstAttempt { get; init; }

        public Task<string> DownloadAsync(
            Uri source,
            string outputDirectory,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadedSources.Add(source);
            if (FailFirstAttempt && DownloadedSources.Count == 1)
                throw new InvalidDataException("Simulated failure.");

            progress?.Report(100);
            return Task.FromResult(Path.Combine(outputDirectory, Path.GetFileName(source.LocalPath)));
        }
    }
}
