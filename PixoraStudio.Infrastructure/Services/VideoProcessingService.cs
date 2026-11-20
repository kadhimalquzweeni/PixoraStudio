using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;
using PixoraStudio.MediaEngine.Readers.Mp4;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Reads MP4 metadata and creates thumbnails using PixoraStudio's managed
/// MP4 reader and H.264 decoder.
/// </summary>
public sealed class VideoProcessingService : IVideoProcessingService
{
    public Task<VideoInfo> GetVideoInfoAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Video file not found.", filePath);

        return Task.Run(() =>
        {
            using FileStream stream = new(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var mediaInfo = new Mp4FileReader().Read(stream);
            return Mp4VideoInfoMapper.Map(mediaInfo, filePath);
        });
    }

    public async Task<string> GenerateThumbnailAsync(
        string filePath,
        TimeSpan position,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        await GenerateThumbnailsAsync(
            filePath,
            [position],
            [outputPath]).ConfigureAwait(false);
        return Path.GetFullPath(outputPath);
    }

    /// <summary>
    /// Extracts multiple filmstrip images from one MP4 parse and one continuous
    /// H.264 decode. Requested frames are captured as the decoder reaches their
    /// presentation timestamps, avoiding repeated GOP decoding for each image.
    /// </summary>
    public Task GenerateThumbnailsAsync(
        string filePath,
        IReadOnlyList<TimeSpan> positions,
        IReadOnlyList<string> outputPaths,
        IProgress<VideoThumbnailResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(outputPaths);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Video file not found.", filePath);
        if (positions.Count != outputPaths.Count)
            throw new ArgumentException(
                "The number of output paths must match the number of thumbnail positions.",
                nameof(outputPaths));

        for (int i = 0; i < positions.Count; i++)
        {
            if (positions[i] < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(positions));
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPaths[i]);
            if (i > 0 && positions[i] < positions[i - 1])
                throw new ArgumentException(
                    "Thumbnail positions must be in ascending order.", nameof(positions));
        }

        if (positions.Count == 0)
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using FileStream stream = new(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Mp4MediaInfo mediaInfo = new Mp4FileReader().Read(stream);
            Mp4MediaTrack videoTrack = mediaInfo
                .GetTracks(Mp4HandlerType.Video)
                .FirstOrDefault()
                ?? throw new NotSupportedException(
                    "The MP4 file does not contain a video track.");

            long presentationEndTicks = GetPresentationEndTicks(videoTrack);
            long[] targetTicks = positions
                .Select(position => ToTrackTicks(position, videoTrack.Timescale, presentationEndTicks))
                .ToArray();

            H264AvcParameterSetCollectionReader parameterSetReader = new();
            H264YuvToBgraConverter converter = new();
            int nextTargetIndex = 0;

            // Decode in one continuous pass. DecodeFromTime per thumbnail
            // replays the GOP from the preceding sync sample, which becomes
            // increasingly expensive as the filmstrip spans a long video.
            H264Mp4TrackDecoder decoder = new();
            foreach (H264Mp4DecodedVideoFrame frame in decoder.Decode(
                stream,
                mediaInfo,
                videoTrack.TrackId,
                cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                long frameEndTicks = checked(
                    frame.PresentationTimeTicks + frame.DurationTicks);

                // Frames are emitted in presentation order. One decoded frame
                // may satisfy duplicate or closely spaced thumbnail targets.
                while (nextTargetIndex < targetTicks.Length &&
                       frameEndTicks > targetTicks[nextTargetIndex])
                {
                    try
                    {
                        SaveThumbnail(
                            frame,
                            outputPaths[nextTargetIndex],
                            parameterSetReader,
                            converter);
                        progress?.Report(new VideoThumbnailResult(
                            nextTargetIndex,
                            Path.GetFullPath(outputPaths[nextTargetIndex])));
                    }
                    catch (Exception exception) when (
                        exception is not OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Thumbnail {nextTargetIndex} could not be generated: {exception}");
                    }

                    nextTargetIndex++;
                }

                if (nextTargetIndex == targetTicks.Length)
                    break;
            }

            if (nextTargetIndex < targetTicks.Length)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"The decoder ended before producing {targetTicks.Length - nextTargetIndex} requested thumbnail(s).");
            }
        }, cancellationToken);
    }

    private static long GetPresentationEndTicks(Mp4MediaTrack track)
    {
        long presentationEndTicks = 0;

        foreach (Mp4SampleLocation sample in track.SampleLocations.Samples)
        {
            long sampleEndTicks = checked(
                sample.PresentationTimeTicks + sample.DurationTicks);
            presentationEndTicks = Math.Max(presentationEndTicks, sampleEndTicks);
        }

        if (presentationEndTicks <= 0)
            throw new InvalidDataException("The MP4 video track has no valid presentation duration.");

        return presentationEndTicks;
    }

    private static long ToTrackTicks(
        TimeSpan position,
        uint timescale,
        long presentationEndTicks)
    {
        if (timescale == 0)
            throw new InvalidDataException("The MP4 video track timescale must be greater than zero.");

        decimal requestedTicks = decimal.Floor(
            (decimal)position.Ticks * timescale / TimeSpan.TicksPerSecond);

        long targetTicks = requestedTicks >= long.MaxValue
            ? long.MaxValue
            : (long)requestedTicks;

        // Match the seek planner's behaviour for positions beyond the end:
        // request the final tick so the last picture is returned.
        return Math.Min(targetTicks, presentationEndTicks - 1);
    }

    private static void SaveThumbnail(
        H264Mp4DecodedVideoFrame frame,
        string outputPath,
        H264AvcParameterSetCollectionReader parameterSetReader,
        H264YuvToBgraConverter converter)
    {
        H264AvcParameterSetCollection parameterSets =
            parameterSetReader.Read(frame.SourceSample.Configuration);
        H264SequenceParameterSet sps =
            parameterSets.SequenceParameterSets.Values.FirstOrDefault()
            ?? throw new InvalidDataException(
                "The AVC configuration does not contain a usable sequence parameter set.");

        H264BgraFrame bgra = converter.Convert(frame.Surface, sps);

        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);

        using SKBitmap source = new(
            bgra.Width,
            bgra.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque);
        Marshal.Copy(bgra.Pixels, 0, source.GetPixels(), bgra.Pixels.Length);

        const int thumbnailWidth = 240;
        int thumbnailHeight = Math.Max(
            1,
            (int)Math.Round((double)bgra.Height * thumbnailWidth / bgra.Width));

        using SKBitmap thumbnail = new(
            thumbnailWidth,
            thumbnailHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque);
        using (SKCanvas canvas = new(thumbnail))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(
                source,
                new SKRect(0, 0, thumbnailWidth, thumbnailHeight));
        }

        using SKImage image = SKImage.FromBitmap(thumbnail);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 90)
            ?? throw new InvalidOperationException(
                "SkiaSharp could not encode the decoded thumbnail.");

        using FileStream output = new(
            fullOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoded.SaveTo(output);
    }
}
