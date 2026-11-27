using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixoraStudio.App.Services;

namespace PixoraStudio.Tests.App;

public sealed class DirectMediaDownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_WritesVideoAndReportsCompletion()
    {
        using var directory = new TemporaryDirectory();
        using var client = CreateClient(CreateVideoResponse());
        var service = new DirectMediaDownloadService(client);
        var progressValues = new List<double>();

        var path = await service.DownloadAsync(
            new Uri("https://media.example/video.mp4"),
            directory.Path,
            new InlineProgress<double>(progressValues.Add),
            CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Equal("video.mp4", System.IO.Path.GetFileName(path));
        Assert.Equal(VideoBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(100d, progressValues[^1]);
    }

    [Fact]
    public async Task DownloadAsync_UsesUniqueFileNameWhenFileAlreadyExists()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory.Path, "video.mp4"), "existing");
        using var client = CreateClient(CreateVideoResponse());
        var service = new DirectMediaDownloadService(client);

        var path = await service.DownloadAsync(
            new Uri("https://media.example/video.mp4"),
            directory.Path,
            null,
            CancellationToken.None);

        Assert.Equal("video (2).mp4", System.IO.Path.GetFileName(path));
        Assert.Equal("existing", await File.ReadAllTextAsync(System.IO.Path.Combine(directory.Path, "video.mp4")));
    }

    [Fact]
    public async Task DownloadAsync_RejectsHtmlResponses()
    {
        using var directory = new TemporaryDirectory();
        using var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not a video</html>")
        });
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var service = new DirectMediaDownloadService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(
            new Uri("https://media.example/video.mp4"),
            directory.Path,
            null,
            CancellationToken.None));

        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task DownloadAsync_RejectsInvalidContainerHeader()
    {
        using var directory = new TemporaryDirectory();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("not a video file"u8.ToArray())
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var client = CreateClient(response);
        var service = new DirectMediaDownloadService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(
            new Uri("https://media.example/video.mp4"),
            directory.Path,
            null,
            CancellationToken.None));

        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task DownloadAsync_RejectsEmptyResponse()
    {
        using var directory = new TemporaryDirectory();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };
        using var client = CreateClient(response);
        var service = new DirectMediaDownloadService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(
            new Uri("https://media.example/video.mp4"),
            directory.Path,
            null,
            CancellationToken.None));

        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task DownloadAsync_RejectsUnsupportedExtensionsBeforeRequest()
    {
        using var directory = new TemporaryDirectory();
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Request should not be sent."));
        using var client = new HttpClient(handler);
        var service = new DirectMediaDownloadService(client);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.DownloadAsync(
            new Uri("https://media.example/stream.m3u8"),
            directory.Path,
            null,
            CancellationToken.None));

        Assert.False(handler.RequestReceived);
    }

    [Fact]
    public async Task DownloadAsync_CancellationDoesNotLeavePartialFiles()
    {
        using var directory = new TemporaryDirectory();
        using var client = CreateClient(CreateVideoResponse());
        var service = new DirectMediaDownloadService(client);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(
            new Uri("https://media.example/video.mp4"),
            directory.Path,
            null,
            cancellation.Token));

        Assert.Empty(Directory.GetFiles(directory.Path));
    }


    [Fact]
    public async Task DownloadResumableAsync_UsesRangeAndIfRangeForValidatedPartialFile()
    {
        using var directory = new TemporaryDirectory();
        var source = new Uri("https://media.example/video.mp4");
        var partialKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri)));
        var partialPath = System.IO.Path.Combine(directory.Path, $".pixora-{partialKey}.part");
        var destinationPath = System.IO.Path.Combine(directory.Path, "video.mp4");
        await File.WriteAllBytesAsync(partialPath, VideoBytes[..8]);
        await File.WriteAllTextAsync(partialPath + ".json", JsonSerializer.Serialize(new
        {
            Source = source.AbsoluteUri,
            DestinationPath = destinationPath,
            FileName = "video.mp4",
            ETag = "\"v1\"",
            LastModifiedUtc = (DateTime?)null,
            ExpectedLength = (long?)VideoBytes.Length
        }));

        long? requestedOffset = null;
        string? ifRangeTag = null;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(VideoBytes[8..])
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(8, VideoBytes.Length - 1, VideoBytes.Length);
            ETag = "\"v1\"",
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            requestedOffset = request.Headers.Range?.Ranges.Single().From;
            ifRangeTag = request.Headers.IfRange?.EntityTag?.Tag;
            return response;
        }));
        var service = new DirectMediaDownloadService(client);

        var result = await service.DownloadResumableAsync(
            source, directory.Path, null, new DownloadControl(), CancellationToken.None);

        Assert.False(result.IsPaused);
        Assert.Equal(8, requestedOffset);
        Assert.Equal("\"v1\"", ifRangeTag);
        Assert.Equal(destinationPath, result.OutputPath);
        Assert.Equal(VideoBytes, await File.ReadAllBytesAsync(destinationPath));
        Assert.False(File.Exists(partialPath + ".json"));
    }

    [Fact]
    public async Task DownloadAsync_ReportsIndeterminateProgressWhenLengthIsUnknown()
    {
        using var directory = new TemporaryDirectory();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(VideoBytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        using var client = CreateClient(response);
        var service = new DirectMediaDownloadService(client);
        var progressValues = new List<double>();

        await service.DownloadAsync(
            new Uri("https://media.example/video.mp4"),
            directory.Path,
            new InlineProgress<double>(progressValues.Add),
            CancellationToken.None);

        Assert.Contains(progressValues, double.IsNaN);
        Assert.Equal(100d, progressValues[^1]);
    }

    private static readonly byte[] VideoBytes =
    [
        0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70,
        0x69, 0x73, 0x6F, 0x6D, 0x00, 0x00, 0x00, 0x00
    ];

    private static HttpResponseMessage CreateVideoResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(VideoBytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        return response;
    }

    private static HttpClient CreateClient(HttpResponseMessage response) =>
        new(new RecordingHandler(_ => response));

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public bool RequestReceived { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestReceived = true;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }


    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "PixoraStudio-DownloaderTests-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
