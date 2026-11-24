namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Quantised spectral coefficients for one scalefactor band and window group.
/// Coefficients remain in AAC grouped transmission order.
/// </summary>
public sealed record AacSpectralBandData(
    int WindowGroup,
    int ScaleFactorBand,
    int CodebookIndex,
    int[] Coefficients);
