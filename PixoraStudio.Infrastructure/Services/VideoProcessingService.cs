using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.General;
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
    /// Extracts multiple filmstrip images from one MP4 parse and one forward
    /// H.264 decode pass. Frames are selected in presentation order, and
    /// decoding stops as soon as the final requested position is satisfied.
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

            int nextTarget = 0;
            H264Mp4TrackDecoder decoder = new();

            foreach (H264Mp4DecodedVideoFrame frame in decoder.Decode(
                stream,
                mediaInfo,
                videoTrack.TrackId,
                cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (frame.Timescale == 0)
                    throw new InvalidDataException(
                        "The decoded video frame has an invalid timescale.");

                double frameEndSeconds =
                    ((double)frame.PresentationTimeTicks + frame.DurationTicks) /
                    frame.Timescale;

                // Decode() yields pictures in presentation order. The first
                // frame whose interval extends beyond a target is the same
                // frame DecodeFromTime() would return for that target.
                while (nextTarget < positions.Count &&
                       positions[nextTarget].TotalSeconds < frameEndSeconds)
                {
                    SaveThumbnail(frame, outputPaths[nextTarget]);
                    progress?.Report(new VideoThumbnailResult(
                        nextTarget, Path.GetFullPath(outputPaths[nextTarget])));
                    nextTarget++;
                }

                if (nextTarget == positions.Count)
                    break;
            }

            if (nextTarget != positions.Count)
                throw new InvalidDataException(
                    $"The decoder reached the end of the video after producing " +
                    $"{nextTarget} of {positions.Count} requested thumbnails.");
        }, cancellationToken);
    }

    private static void SaveThumbnail(
        H264Mp4DecodedVideoFrame frame,
        string outputPath)
    {
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(
                frame.SourceSample.Configuration);
        H264SequenceParameterSet sps =
            parameterSets.SequenceParameterSets.Values.FirstOrDefault()
            ?? throw new InvalidDataException(
                "The AVC configuration does not contain a usable sequence parameter set.");

        H264BgraFrame bgra =
            new H264YuvToBgraConverter().Convert(frame.Surface, sps);

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
