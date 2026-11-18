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

        uint descriptionIndex = sample.Location.SampleDescriptionIndex;
        if (descriptionIndex == 0)
            throw new FormatException("The MP4 sample has no sample-description index.");

        Mp4SampleDescription? description = track.SampleDescriptions
            .SingleOrDefault(item => item.Index == descriptionIndex);
        if (description is null)
            throw new FormatException(
                $"The MP4 sample references missing sample description {descriptionIndex}.");
        if (description.Video is not { Codec: Mp4VideoCodec.Avc, CodecConfiguration: not null } video)
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
            (double)location.DecodeTimeTicks / track.Timescale,
            (double)location.PresentationTimeTicks / track.Timescale,
            (double)location.DurationTicks / track.Timescale,
            location.IsSyncSample,
            configuration,
            nalUnits);
    }
}
