using PixoraStudio.Core.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Maps parsed MP4 track metadata into the editor's presentation model.
/// </summary>
public static class Mp4VideoInfoMapper
{
    public static VideoInfo Map(Mp4MediaInfo mediaInfo, string filePath)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Mp4MediaTrack track = mediaInfo.GetTracks(Mp4HandlerType.Video)
            .FirstOrDefault()
            ?? throw new NotSupportedException("The MP4 file does not contain a video track.");

        Mp4VideoSampleEntry videoEntry = track.Track.SampleDescriptions
            .Select(description => description.Video)
            .FirstOrDefault(entry => entry is not null)
            ?? throw new NotSupportedException("The MP4 video track has no supported visual sample description.");

        if (videoEntry.Codec != Mp4VideoCodec.Avc ||
            videoEntry.Format is not ("avc1" or "avc3"))
        {
            throw new NotSupportedException(
                $"The native video editor currently supports AVC/H.264 MP4 video, not {videoEntry.Format}.");
        }

        double durationSeconds = track.Track.DurationSeconds;
        if (durationSeconds <= 0 || !double.IsFinite(durationSeconds))
            durationSeconds = mediaInfo.DurationSeconds;

        if (durationSeconds <= 0 || !double.IsFinite(durationSeconds))
            throw new InvalidDataException("The MP4 video duration is missing or invalid.");

        double frameRate = track.SampleCount / durationSeconds;
        if (!double.IsFinite(frameRate) || frameRate <= 0)
            frameRate = 0;

        return new VideoInfo
        {
            FilePath = Path.GetFullPath(filePath),
            FileName = Path.GetFileName(filePath),
            Duration = TimeSpan.FromSeconds(durationSeconds),
            Width = videoEntry.Width,
            Height = videoEntry.Height,
            FramesPerSecond = frameRate
        };
    }
}
