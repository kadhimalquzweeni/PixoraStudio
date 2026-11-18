using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parsed sequence and picture parameter sets extracted from an AVC decoder
/// configuration record.
/// </summary>
public sealed record H264AvcParameterSetCollection(
    H264AvcDecoderConfiguration Configuration,
    IReadOnlyDictionary<uint, H264SequenceParameterSet> SequenceParameterSets,
    IReadOnlyDictionary<uint, H264PictureParameterSetHeader> PictureParameterSets);
