namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Describes the location and timing of an encoded MP4 sample.
/// </summary>
public sealed record Mp4SampleLocation(
    uint SampleNumber,
    uint ChunkNumber,
    ulong Offset,
    uint Size,
    ulong DecodeTimeTicks,
    uint DurationTicks,
    bool IsSyncSample)
{
    public int CompositionOffsetTicks { get; init; }

    /// <summary>
    /// One-based index into the track's sample description table.
    /// </summary>
    public uint SampleDescriptionIndex { get; init; }

    public long PresentationTimeTicks =>
        checked((long)DecodeTimeTicks + CompositionOffsetTicks);
}