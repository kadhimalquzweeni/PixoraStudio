using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Slice-level controls consumed by the in-loop deblocking process.</summary>
public sealed record H264DeblockingSliceParameters(
    int DisableDeblockingFilterIdc,
    int AlphaOffsetDiv2,
    int BetaOffsetDiv2)
{
    public static H264DeblockingSliceParameters FromSliceHeader(H264SliceHeader slice)
    {
        ArgumentNullException.ThrowIfNull(slice);
        return new H264DeblockingSliceParameters(
            checked((int)(slice.DisableDeblockingFilterIdc ?? 0)),
            slice.SliceAlphaC0OffsetDiv2 ?? 0,
            slice.SliceBetaOffsetDiv2 ?? 0);
    }

    public void Validate()
    {
        if (DisableDeblockingFilterIdc is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(DisableDeblockingFilterIdc));
        if (AlphaOffsetDiv2 is < -6 or > 6)
            throw new ArgumentOutOfRangeException(nameof(AlphaOffsetDiv2));
        if (BetaOffsetDiv2 is < -6 or > 6)
            throw new ArgumentOutOfRangeException(nameof(BetaOffsetDiv2));
    }
}
