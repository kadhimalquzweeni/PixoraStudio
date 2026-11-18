
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PixoraStudio.Infrastructure.Services;

public class FFmpegService
{
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;

    public FFmpegService(
        string? applicationDirectory = null,
        string? pathEnvironment = null)
    {
        string baseDirectory = applicationDirectory ?? AppContext.BaseDirectory;
        string? searchPath = pathEnvironment ?? Environment.GetEnvironmentVariable("PATH");

        _ffmpegPath = FFmpegExecutableResolver.Resolve(
            "ffmpeg.exe", baseDirectory, searchPath) ?? string.Empty;

        _ffprobePath = FFmpegExecutableResolver.Resolve(
            "ffprobe.exe", baseDirectory, searchPath) ?? string.Empty;
    }

    public string FFmpegPath => _ffmpegPath;
    public string FFprobePath => _ffprobePath;

    public bool IsAvailable =>
        File.Exists(_ffmpegPath) &&
        File.Exists(_ffprobePath);

    public void EnsureFFmpegAvailable()
    {
        if (!File.Exists(_ffmpegPath))
        {
            throw new FileNotFoundException(
                "Pixora Studio could not find ffmpeg.exe. Place ffmpeg.exe and ffprobe.exe in the application's FFmpeg folder, or install FFmpeg and add its bin folder to the Windows PATH environment variable.",
                _ffmpegPath);
        }
    }

    public void EnsureFFprobeAvailable()
    {
        if (!File.Exists(_ffprobePath))
        {
            throw new FileNotFoundException(
                "Pixora Studio could not find ffprobe.exe. Place ffmpeg.exe and ffprobe.exe in the application's FFmpeg folder, or install FFmpeg and add its bin folder to the Windows PATH environment variable.",
                _ffprobePath);
        }
    }

    public async Task<string> ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken = default)
    {
        EnsureFFmpegAvailable();

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync(
            cancellationToken);

        string error = await process.StandardError.ReadToEndAsync(
            cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"FFmpeg failed: {error}");
        }

        return output;
    }

    // Export a selected section of a video with progress reporting.
    public async Task ExportTrimmedVideoAsync(
        string inputPath,
        string outputPath,
        TimeSpan startTime,
        TimeSpan endTime,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_ffmpegPath))
            throw new FileNotFoundException(
                "FFmpeg executable was not found.",
                _ffmpegPath);

        if (!File.Exists(inputPath))
            throw new FileNotFoundException(
                "The source video was not found.",
                inputPath);

        if (startTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(startTime));

        if (endTime <= startTime)
            throw new ArgumentException(
                "The trim end must be greater than the trim start.");

        string? outputDirectory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        TimeSpan clipDuration = endTime - startTime;

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // FFmpeg arguments are added individually to avoid quoting issues.
        startInfo.ArgumentList.Add("-y");

        // Input seeking.
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(
            startTime.TotalSeconds.ToString(
                "0.######",
                CultureInfo.InvariantCulture));

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);

        // Duration of the exported clip.
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(
            clipDuration.TotalSeconds.ToString(
                "0.######",
                CultureInfo.InvariantCulture));

        // Accurate trimming through re-encoding.
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libx264");

        startInfo.ArgumentList.Add("-preset");
        startInfo.ArgumentList.Add("medium");

        startInfo.ArgumentList.Add("-crf");
        startInfo.ArgumentList.Add("18");

        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("aac");

        startInfo.ArgumentList.Add("-b:a");
        startInfo.ArgumentList.Add("192k");

        // Make MP4 suitable for streaming and playback.
        startInfo.ArgumentList.Add("-movflags");
        startInfo.ArgumentList.Add("+faststart");

        // FFmpeg progress output.
        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");

        startInfo.ArgumentList.Add("-nostats");

        startInfo.ArgumentList.Add(outputPath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        // Read both streams concurrently to prevent deadlocks.
        Task<string> errorTask =
            process.StandardError.ReadToEndAsync();

        try
        {
            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? line = await process.StandardOutput.ReadLineAsync(
                    cancellationToken);

                if (line == null)
                    break;

                if (line.StartsWith(
                    "out_time=",
                    StringComparison.Ordinal))
                {
                    string timeValue = line["out_time=".Length..];

                    if (TimeSpan.TryParse(
                        timeValue,
                        CultureInfo.InvariantCulture,
                        out TimeSpan outputTime))
                    {
                        double percentage =
                            outputTime.TotalMilliseconds /
                            clipDuration.TotalMilliseconds * 100.0;

                        percentage = Math.Clamp(
                            percentage, 0, 100);

                        progress?.Report(percentage);
                    }
                }

                if (line == "progress=end")
                {
                    progress?.Report(100);
                }
            }

            await process.WaitForExitAsync(
                cancellationToken);

            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"FFmpeg export failed: {error}");
            }

            progress?.Report(100);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();

            throw;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();

            throw;
        }
    }
}