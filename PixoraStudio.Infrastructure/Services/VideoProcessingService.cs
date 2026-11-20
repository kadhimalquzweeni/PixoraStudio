using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
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
    private readonly Mp4FileReader _fileReader = new();
    private readonly H264Mp4TrackDecoder _videoDecoder = new();
    private readonly H264AvcParameterSetCollectionReader _parameterSetReader = new();
    private readonly H264YuvToBgraConverter _colourConverter = new();

    public Task<VideoInfo> GetVideoInfoAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Video file not found.", filePath);

        return Task.Run(() =>
        {
            using FileStream stream = new(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var mediaInfo = _fileReader.Read(stream);
            return Mp4VideoInfoMapper.Map(mediaInfo, filePath);
        });
    }

    public Task<string> GenerateThumbnailAsync(
        string filePath,
        TimeSpan position,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Video file not found.", filePath);
        if (position < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(position));

        return Task.Run(() =>
        {
            using FileStream stream = new(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var mediaInfo = _fileReader.Read(stream);
            var videoTrack = mediaInfo.GetTracks(
                PixoraStudio.MediaEngine.Models.General.Mp4HandlerType.Video)
                .FirstOrDefault()
                ?? throw new NotSupportedException("The MP4 file does not contain a video track.");

            var frame = _videoDecoder.DecodeFromTime(
                    stream,
                    mediaInfo,
                    videoTrack.TrackId,
                    position.TotalSeconds)
                .FirstOrDefault()
                ?? throw new InvalidDataException("The managed H.264 decoder could not produce a frame at the requested position.");

            var parameterSets = _parameterSetReader.Read(
                frame.SourceSample.Configuration);
            var sps = parameterSets.SequenceParameterSets.Values.FirstOrDefault()
                ?? throw new InvalidDataException("The AVC configuration does not contain a usable sequence parameter set.");

            H264BgraFrame bgra = _colourConverter.Convert(frame.Surface, sps);
            string fullOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);

            using SKBitmap bitmap = new(
                bgra.Width,
                bgra.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Opaque);

            Marshal.Copy(
                bgra.Pixels,
                0,
                bitmap.GetPixels(),
                bgra.Pixels.Length);

            using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("SkiaSharp could not encode the decoded thumbnail.");

            using FileStream output = new(
                fullOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoded.SaveTo(output);
            return fullOutputPath;
        });
    }
}
