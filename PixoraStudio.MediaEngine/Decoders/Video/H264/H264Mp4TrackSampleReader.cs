using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Resolves an encoded MP4 sample's sample description, parses its avcC
/// configuration, splits its length-prefixed NAL units and preserves timing.
/// </summary>
public sealed class H264Mp4TrackSampleReader
{
    private readonly H264AvcDecoderConfigurationReader _configurationReader = new();
    private readonly H264Mp4SampleNalUnitReader _sampleNalUnitReader = new();

    public H264Mp4VideoSample Read(Mp4Track track, Mp4EncodedSample sample)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(sample);

        if (track.HandlerType != Mp4HandlerType.Video)
            throw new ArgumentException("The MP4 track is not a video track.", nameof(track));
        if (track.Timescale == 0)
            throw new FormatException("The MP4 video track timescale must be greater than zero.");
        if (sample.Location.SampleNumber == 0)
            throw new FormatException("MP4 sample numbers are one-based.");
        if (sample.Location.Size != (uint)sample.Data.Length)
            throw new FormatException("The encoded sample byte count does not match its sample-table size.");

        long presentationTimeTicks;
        try
        {
            presentationTimeTicks = sample.Location.PresentationTimeTicks;
        }
        catch (OverflowException exception)
        {
            throw new FormatException("The MP4 sample presentation timestamp exceeds the supported range.", exception);
        }

        uint descriptionIndex = sample.Location.SampleDescriptionIndex;
        if (descriptionIndex == 0)
            throw new FormatException("The MP4 sample has no sample-description index.");

        Mp4SampleDescription[] descriptions = track.SampleDescriptions
            .Where(item => item.Index == descriptionIndex)
            .Take(2)
            .ToArray();
        if (descriptions.Length == 0)
            throw new FormatException(
                $"The MP4 sample references missing sample description {descriptionIndex}.");
        if (descriptions.Length > 1)
            throw new FormatException(
                $"The MP4 track contains duplicate sample description index {descriptionIndex}.");

        Mp4VideoSampleEntry? video = descriptions[0].Video;
        if (video is null ||
            (video.Format != "avc1" && video.Format != "avc3") ||
            video.Codec != Mp4VideoCodec.Avc ||
            video.CodecConfiguration is null)
            throw new NotSupportedException(
                $"Sample description {descriptionIndex} is not configured for AVC video.");

        H264AvcDecoderConfiguration configuration =
            _configurationReader.Read(video.CodecConfiguration);
        IReadOnlyList<H264NalUnit> nalUnits =
            _sampleNalUnitReader.Read(sample, configuration);

        Mp4SampleLocation location = sample.Location;
        return new H264Mp4VideoSample(
            track.TrackId,
            location.SampleNumber,
            location.DecodeTimeTicks,
            presentationTimeTicks,
            location.DurationTicks,
            track.Timescale,
            location.IsSyncSample,
            configuration,
            nalUnits);
    }
}
