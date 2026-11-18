
using System.Diagnostics;
using System.Globalization;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;

namespace PixoraStudio.Infrastructure.Services;

public class VideoProcessingService : IVideoProcessingService
{
    private readonly FFmpegService _ffmpeg;

    public VideoProcessingService(FFmpegService ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    public async Task<VideoInfo> GetVideoInfoAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Video file not found.", filePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpeg.FFprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-select_streams");
        startInfo.ArgumentList.Add("v:0");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add(
            "stream=codec_type,width,height,avg_frame_rate,r_frame_rate,duration,duration_ts,time_base:format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(filePath);

        using var process = new Process { StartInfo = startInfo };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException(
                "FFprobe took too long to read the video.");
        }

        string output = await outputTask;
        string error = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFprobe failed: {error}");

        return FFprobeOutputParser.Parse(output, filePath);
    }

    public async Task<string> GenerateThumbnailAsync(
        string filePath,
        TimeSpan position,
        string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpeg.FFmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(
            position.TotalSeconds.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(filePath);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add("scale=640:-1");
        startInfo.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = startInfo };

        process.Start();

        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException(
                "FFmpeg took too long to generate the thumbnail.");
        }

        string error = await errorTask;
        await outputTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed: {error}");

        if (!File.Exists(outputPath))
            throw new FileNotFoundException(
                "FFmpeg did not generate the thumbnail.");

        return outputPath;
    }

    private static double ParseFrameRate(string value)
    {
        string[] parts = value.Split('/');

        if (parts.Length != 2)
            return 0;

        double numerator = double.Parse(
            parts[0], CultureInfo.InvariantCulture);

        double denominator = double.Parse(
            parts[1], CultureInfo.InvariantCulture);

        return denominator == 0 ? 0 : numerator / denominator;
    }
}
