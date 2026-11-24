namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Describes one AAC section: a contiguous range of scalefactor bands
/// in a single window group that shares one section codebook.
/// </summary>
public sealed record AacSectionInfo(
    int WindowGroup,
    int StartScaleFactorBand,
    int ScaleFactorBandCount,
    int CodebookIndex)
{
    public int EndScaleFactorBandExclusive =>
        checked(StartScaleFactorBand + ScaleFactorBandCount);
}
