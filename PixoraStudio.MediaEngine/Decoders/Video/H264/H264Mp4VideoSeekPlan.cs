namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Describes an MP4 AVC seek. Decoding starts at the preceding random-access
/// sample, while output begins with the first presentation interval containing
/// (or following) the requested timestamp.
/// </summary>
public sealed record H264Mp4VideoSeekPlan(
    uint FirstSampleToDecode,
    long TargetPresentationTimeTicks,
    uint Timescale)
{
    public double TargetPresentationTimeSeconds =>
        (double)TargetPresentationTimeTicks / Timescale;
}
