using PixoraStudio.Infrastructure.Services;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class FFmpegExecutableResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PixoraStudio_FFmpegTests_" + Guid.NewGuid());

    public FFmpegExecutableResolverTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Resolve_ShouldPreferBundledExecutableOverPath()
    {
        string bundledDirectory = Path.Combine(_root, "FFmpeg");
        string pathDirectory = Path.Combine(_root, "path");
        Directory.CreateDirectory(bundledDirectory);
        Directory.CreateDirectory(pathDirectory);

        string bundled = Path.Combine(bundledDirectory, "ffprobe.exe");
        File.WriteAllText(bundled, "bundled");
        File.WriteAllText(Path.Combine(pathDirectory, "ffprobe.exe"), "path");

        string? result = FFmpegExecutableResolver.Resolve(
            "ffprobe.exe", _root, pathDirectory);

        Assert.Equal(Path.GetFullPath(bundled), result);
    }

    [Fact]
    public void Resolve_ShouldFindExecutableOnPath()
    {
        string pathDirectory = Path.Combine(_root, "path");
        Directory.CreateDirectory(pathDirectory);
        string executable = Path.Combine(pathDirectory, "ffmpeg.exe");
        File.WriteAllText(executable, "test");

        string? result = FFmpegExecutableResolver.Resolve(
            "ffmpeg.exe", _root, pathDirectory);

        Assert.Equal(Path.GetFullPath(executable), result);
    }

    [Fact]
    public void Resolve_ShouldReturnNullWhenExecutableIsUnavailable()
    {
        string? result = FFmpegExecutableResolver.Resolve(
            "ffprobe.exe", _root, null);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_ShouldIgnoreInvalidPathEntriesAndContinueSearching()
    {
        string pathDirectory = Path.Combine(_root, "valid-path");
        Directory.CreateDirectory(pathDirectory);
        string executable = Path.Combine(pathDirectory, "ffprobe.exe");
        File.WriteAllText(executable, "test");

        string pathValue = string.Join(
            Path.PathSeparator,
            "\0invalid\0",
            pathDirectory);

        string? result = FFmpegExecutableResolver.Resolve(
            "ffprobe.exe", _root, pathValue);

        Assert.Equal(Path.GetFullPath(executable), result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
