using PixoraStudio.Infrastructure.Services;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class FFmpegServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PixoraStudio_FFmpegServiceTests_" + Guid.NewGuid());

    public FFmpegServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Constructor_ShouldResolveBothExecutablesFromPath()
    {
        string pathDirectory = Path.Combine(_root, "bin");
        Directory.CreateDirectory(pathDirectory);
        string ffmpeg = Path.Combine(pathDirectory, "ffmpeg.exe");
        string ffprobe = Path.Combine(pathDirectory, "ffprobe.exe");
        File.WriteAllText(ffmpeg, "ffmpeg");
        File.WriteAllText(ffprobe, "ffprobe");

        var service = new FFmpegService(_root, pathDirectory);

        Assert.Equal(Path.GetFullPath(ffmpeg), service.FFmpegPath);
        Assert.Equal(Path.GetFullPath(ffprobe), service.FFprobePath);
        Assert.True(service.IsAvailable);
    }

    [Fact]
    public void Constructor_ShouldReportUnavailableWhenOneExecutableIsMissing()
    {
        string pathDirectory = Path.Combine(_root, "bin");
        Directory.CreateDirectory(pathDirectory);
        File.WriteAllText(Path.Combine(pathDirectory, "ffmpeg.exe"), "ffmpeg");

        var service = new FFmpegService(_root, pathDirectory);

        Assert.False(service.IsAvailable);
        Assert.Throws<FileNotFoundException>(
            service.EnsureFFprobeAvailable);
    }

    [Fact]
    public void EnsureFFmpegAvailable_ShouldExplainHowToInstallMissingDependency()
    {
        var service = new FFmpegService(_root, null);

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(
            service.EnsureFFmpegAvailable);

        Assert.Contains("PATH", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FFmpeg", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
