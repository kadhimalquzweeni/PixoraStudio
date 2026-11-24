using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Applies decoded AAC scalefactors to inverse-quantised spectral coefficients.
/// Noise and intensity bands are left at zero for their dedicated later stages.
/// </summary>
public sealed class AacSpectralDequantizer
{
    private readonly AacInverseQuantizer _inverseQuantizer = new();

    public IReadOnlyList<AacDequantizedSpectralBandData> Dequantize(
        IReadOnlyList<AacSpectralBandData> spectralBands,
        AacScaleFactorData scaleFactors)
    {
        ArgumentNullException.ThrowIfNull(spectralBands);
        ArgumentNullException.ThrowIfNull(scaleFactors);

        Dictionary<(int Group, int Band), AacScaleFactorBandValue> values =
            scaleFactors.Bands.ToDictionary(
                value => (value.WindowGroup, value.ScaleFactorBand));

        List<AacDequantizedSpectralBandData> result = new(spectralBands.Count);
        foreach (AacSpectralBandData band in spectralBands)
        {
            ArgumentNullException.ThrowIfNull(band);
            if (band.CodebookIndex is < 0 or > 15 || band.CodebookIndex == 12)
                throw new InvalidDataException("Spectral band uses a reserved AAC codebook.");

            float[] coefficients = new float[band.Coefficients.Length];

            if (band.CodebookIndex is >= 1 and <= 11)
            {
                if (!values.TryGetValue((band.WindowGroup, band.ScaleFactorBand), out AacScaleFactorBandValue? scaleFactor) ||
                    scaleFactor.CodebookIndex != band.CodebookIndex)
                {
                    throw new InvalidDataException(
                        $"Missing scalefactor for spectral band {band.ScaleFactorBand}, group {band.WindowGroup}.");
                }

                // AAC spectral reconstruction: |q|^(4/3) * 2^((sf - 100) / 4).
                double gain = Math.Pow(2.0, (scaleFactor.Value - 100) / 4.0);
                for (int i = 0; i < band.Coefficients.Length; i++)
                    coefficients[i] = (float)(_inverseQuantizer.Dequantize(band.Coefficients[i]) * gain);
            }
            // Zero, PNS and intensity codebooks are populated by later stages.

            result.Add(new AacDequantizedSpectralBandData(
                band.WindowGroup, band.ScaleFactorBand, band.CodebookIndex, coefficients));
        }

        return result.AsReadOnly();
    }
}