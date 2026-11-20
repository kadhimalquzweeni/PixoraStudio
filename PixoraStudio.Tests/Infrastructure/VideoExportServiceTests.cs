using PixoraStudio.Infrastructure.Services;
using PixoraStudio.MediaEngine.Exceptions;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class VideoExportServiceTests
{
    [Fact]
    public async Task ExportTrimmedVideoAsync_ShouldRejectMissingSource()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            VideoExportService service = new();

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.ExportTrimmedVideoAsync(
                    Path.Combine(directory, "missing.mp4"),
                    Path.Combine(directory, "output.mp4"),
                    0,
                    1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportTrimmedVideoAsync_ShouldRejectSameSourceAndDestination()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, [1, 2, 3]);

            VideoExportService service = new();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ExportTrimmedVideoAsync(source, source, 0, 1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportTrimmedVideoAsync_ShouldRequireMp4Extension()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, [1, 2, 3]);

            VideoExportService service = new();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ExportTrimmedVideoAsync(
                    source,
                    Path.Combine(directory, "output.mov"),
                    0,
                    1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportTrimmedVideoAsync_ShouldPreserveExistingDestinationWhenExportFails()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string source = Path.Combine(directory, "source.mp4");
            string output = Path.Combine(directory, "output.mp4");
            byte[] originalOutput = [90, 91, 92];

            await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(output, originalOutput);

            VideoExportService service = new();

            await Assert.ThrowsAsync<Mp4ParsingException>(() =>
                service.ExportTrimmedVideoAsync(source, output, 0, 1));

            Assert.Equal(originalOutput, await File.ReadAllBytesAsync(output));
            Assert.Equal(
                new[] { "output.mp4", "source.mp4" },
                Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(name => name));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportTrimmedVideoAsync_ShouldCleanTemporaryFileWhenCancelled()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string source = Path.Combine(directory, "source.mp4");
            string output = Path.Combine(directory, "output.mp4");
            await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);

            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            VideoExportService service = new();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.ExportTrimmedVideoAsync(
                    source,
                    output,
                    0,
                    1,
                    cancellationToken: cancellation.Token));

            Assert.False(File.Exists(output));
            Assert.Equal(new[] { "source.mp4" }, Directory.GetFiles(directory).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"PixoraExportTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
