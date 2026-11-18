using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// An AVC MP4 sample split into NAL units while retaining exact track-clock
/// timing from its sample-table location.
/// </summary>
public sealed record H264Mp4VideoSample(
    uint TrackId,
    uint SampleNumber,
    ulong DecodeTimeTicks,
    long PresentationTimeTicks,
    uint DurationTicks,
    uint Timescale,
    bool IsSyncSample,
    H264AvcDecoderConfiguration Configuration,
    IReadOnlyList<H264NalUnit> NalUnits)
{
    public double DecodeTimeSeconds => (double)DecodeTimeTicks / Timescale;
    public double PresentationTimeSeconds => (double)PresentationTimeTicks / Timescale;
    public double DurationSeconds => (double)DurationTicks / Timescale;
}
