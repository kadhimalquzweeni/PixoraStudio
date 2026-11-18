using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Splits one MP4 AVC sample into NAL units using the NAL length-field width
/// declared by its avcC decoder configuration record.
/// </summary>
public sealed class H264Mp4SampleNalUnitReader
{
    private readonly H264NalUnitReader _nalUnitReader = new();

    public IReadOnlyList<H264NalUnit> Read(
        Mp4EncodedSample sample,
        H264AvcDecoderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(configuration);
        IReadOnlyList<H264NalUnit> units = _nalUnitReader.ReadLengthPrefixed(
            sample.Data, configuration.NaluLengthSize);
        if (units.Count == 0)
            throw new FormatException("The MP4 AVC sample contains no NAL units.");
        return units;
    }
}
