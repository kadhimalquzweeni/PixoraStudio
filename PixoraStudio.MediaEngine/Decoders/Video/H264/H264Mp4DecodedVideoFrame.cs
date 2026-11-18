namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// A POC-reordered decoded surface paired with the exact MP4 sample whose
/// timestamps belong to that picture.
/// </summary>
public sealed record H264Mp4DecodedVideoFrame(
    H264DecodedPictureSurface Surface,
    int PictureOrderCount,
    H264Mp4VideoSample SourceSample)
{
    public uint TrackId => SourceSample.TrackId;
    public uint SampleNumber => SourceSample.SampleNumber;
    public ulong DecodeTimeTicks => SourceSample.DecodeTimeTicks;
    public long PresentationTimeTicks => SourceSample.PresentationTimeTicks;
    public uint DurationTicks => SourceSample.DurationTicks;
    public uint Timescale => SourceSample.Timescale;
}
