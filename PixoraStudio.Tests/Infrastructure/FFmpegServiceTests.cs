using PixoraStudio.Infrastructure.Services;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class FFmpegServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PixoraStudio_FFmpegServiceTests_" + Guid.NewGuid());

    public FFmpegServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Constructor_ShouldResolveBothExecutablesFromPath()
    {
        string bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        string ffmpeg = Path.Combine(bin, "ffmpeg.exe");
        string ffprobe = Path.Combine(bin, "ffprobe.exe");
        File.WriteAllText(ffmpeg, "fake");
        File.WriteAllText(ffprobe, "fake");
        var service = new FFmpegService(_root, bin);
        Assert.Equal(Path.GetFullPath(ffmpeg), service.FFmpegPath);
        Assert.Equal(Path.GetFullPath(ffprobe), service.FFprobePath);
        Assert.True(service.IsAvailable);
    }

    [Fact]
    public void IsAvailable_ShouldRequireBothExecutables()
    {
        string bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "ffmpeg.exe"), "fake");
        Assert.False(new FFmpegService(_root, bin).IsAvailable);
    }

    [Fact]
    public async Task ExportTrimmedVideoAsync_ShouldRejectMissingInput()
    {
        var service = CreateAvailableService();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.ExportTrimmedVideoAsync(Path.Combine(_root, "missing.mp4"), Path.Combine(_root, "out.mp4"), TimeSpan.Zero, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ExportTrimmedVideoAsync_ShouldRejectNegativeStart()
    {
        var service = CreateAvailableService();
        string input = Path.Combine(_root, "input.mp4");
        File.WriteAllText(input, "placeholder");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ExportTrimmedVideoAsync(input, Path.Combine(_root, "out.mp4"), TimeSpan.FromMilliseconds(-1), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ExportTrimmedVideoAsync_ShouldRejectEndBeforeOrAtStart()
    {
        var service = CreateAvailableService();
        string input = Path.Combine(_root, "input.mp4");
        File.WriteAllText(input, "placeholder");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExportTrimmedVideoAsync(input, Path.Combine(_root, "out.mp4"), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)));
    }

    private FFmpegService CreateAvailableService()
    {
        string bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "ffmpeg.exe"), "fake");
        File.WriteAllText(Path.Combine(bin, "ffprobe.exe"), "fake");
        return new FFmpegService(_root, bin);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
