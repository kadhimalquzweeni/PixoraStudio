namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parameter-set NAL units and framing metadata extracted from an AVC
/// Decoder Configuration Record (avcC) in an MP4 visual sample entry.
/// </summary>
public sealed record H264AvcDecoderConfiguration(
    byte Profile,
    byte Compatibility,
    byte Level,
    int NaluLengthSize,
    IReadOnlyList<H264NalUnit> SequenceParameterSets,
    IReadOnlyList<H264NalUnit> PictureParameterSets,
    IReadOnlyList<H264NalUnit> SequenceParameterSetExtensions)
{
    public byte? ChromaFormat { get; init; }
    public byte? BitDepthLumaMinus8 { get; init; }
    public byte? BitDepthChromaMinus8 { get; init; }
}
