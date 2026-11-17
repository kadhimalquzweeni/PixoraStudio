namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Floating-point inverse-quantised coefficients for one AAC scalefactor band
/// and window group. Values remain in grouped spectral transmission order.
/// </summary>
public sealed record AacDequantizedSpectralBandData(
    int WindowGroup,
    int ScaleFactorBand,
    int CodebookIndex,
    float[] Coefficients);