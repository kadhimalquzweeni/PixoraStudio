using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// An AVC MP4 sample split into NAL units while retaining decode and
/// presentation timing from its sample-table location.
/// </summary>
public sealed record H264Mp4VideoSample(
    uint TrackId,
    uint SampleNumber,
    double DecodeTimeSeconds,
    double PresentationTimeSeconds,
    double DurationSeconds,
    bool IsSyncSample,
    H264AvcDecoderConfiguration Configuration,
    IReadOnlyList<H264NalUnit> NalUnits);
