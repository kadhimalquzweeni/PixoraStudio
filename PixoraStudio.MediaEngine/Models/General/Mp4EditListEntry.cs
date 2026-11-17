namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents one entry in an MP4 edit list.
/// </summary>
public sealed record Mp4EditListEntry(
    ulong SegmentDuration,
    long MediaTime,
    short MediaRateInteger,
    short MediaRateFraction)
{
    public bool IsEmptyEdit => MediaTime == -1;

    public bool UsesNormalPlaybackRate =>
        MediaRateInteger == 1 &&
        MediaRateFraction == 0;
}