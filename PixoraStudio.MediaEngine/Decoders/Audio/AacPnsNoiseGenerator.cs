using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reconstructs AAC perceptual-noise-substitution bands. Each individual
/// window's random vector is normalised to unit energy before its decoded
/// noise scalefactor is applied.
/// </summary>
public sealed class AacPnsNoiseGenerator
{
    private readonly IAacNoiseSource _noiseSource;

    public AacPnsNoiseGenerator(IAacNoiseSource? noiseSource = null)
    {
        _noiseSource = noiseSource ?? new AacLcgNoiseSource();
    }

    public IReadOnlyList<AacDequantizedSpectralBandData> Apply(
        IReadOnlyList<AacDequantizedSpectralBandData> spectralBands,
        AacScaleFactorData scaleFactors,
        IReadOnlyList<int> scaleFactorBandOffsets,
        AacIndividualChannelStreamInfo streamInfo)
    {
        ArgumentNullException.ThrowIfNull(spectralBands);
        ArgumentNullException.ThrowIfNull(scaleFactors);
        ArgumentNullException.ThrowIfNull(scaleFactorBandOffsets);
        ArgumentNullException.ThrowIfNull(streamInfo);

        int windowLength = streamInfo.UsesShortWindows ? 128 : 1024;
        if (scaleFactorBandOffsets.Count < 2 ||
            scaleFactorBandOffsets[0] != 0 ||
            scaleFactorBandOffsets[^1] != windowLength)
        {
            throw new ArgumentException(
                $"Scalefactor-band offsets must span 0..{windowLength}.",
                nameof(scaleFactorBandOffsets));
        }

        for (int i = 1; i < scaleFactorBandOffsets.Count; i++)
        {
            if (scaleFactorBandOffsets[i] <= scaleFactorBandOffsets[i - 1])
                throw new ArgumentException(
                    "Scalefactor-band offsets must be strictly increasing.",
                    nameof(scaleFactorBandOffsets));
        }

        IReadOnlyList<int> groupLengths = streamInfo.WindowGroupLengths;
        Dictionary<(int Group, int Band), AacScaleFactorBandValue> scaleFactorMap =
            scaleFactors.Bands.ToDictionary(
                value => (value.WindowGroup, value.ScaleFactorBand));

        List<AacDequantizedSpectralBandData> result = new(spectralBands.Count);
        foreach (AacDequantizedSpectralBandData band in spectralBands)
        {
            ArgumentNullException.ThrowIfNull(band);
            float[] coefficients = (float[])band.Coefficients.Clone();

            if (band.CodebookIndex == 13)
            {
                if ((uint)band.WindowGroup >= (uint)groupLengths.Count ||
                    (uint)band.ScaleFactorBand >= (uint)(scaleFactorBandOffsets.Count - 1))
                {
                    throw new InvalidDataException(
                        "PNS band references an invalid window group or scalefactor band.");
                }

                if (!scaleFactorMap.TryGetValue(
                        (band.WindowGroup, band.ScaleFactorBand),
                        out AacScaleFactorBandValue? scaleFactor) ||
                    scaleFactor.CodebookIndex != 13)
                {
                    throw new InvalidDataException(
                        $"Missing PNS energy scalefactor for band {band.ScaleFactorBand}, group {band.WindowGroup}.");
                }

                int bandWidth =
                    scaleFactorBandOffsets[band.ScaleFactorBand + 1] -
                    scaleFactorBandOffsets[band.ScaleFactorBand];
                int expectedLength = checked(bandWidth * groupLengths[band.WindowGroup]);

                if (coefficients.Length != expectedLength)
                    throw new InvalidDataException(
                        "PNS coefficient count does not match the grouped scalefactor-band width.");

                double gain = Math.Pow(2.0, (scaleFactor.Value - 100) / 4.0);
                for (int window = 0; window < groupLengths[band.WindowGroup]; window++)
                {
                    int offset = window * bandWidth;
                    double energy = 0;

                    for (int i = 0; i < bandWidth; i++)
                    {
                        float sample = _noiseSource.NextSample();
                        if (!float.IsFinite(sample) || sample < -1f || sample > 1f)
                            throw new InvalidDataException(
                                "AAC noise source returned a sample outside the finite [-1, 1] range.");

                        coefficients[offset + i] = sample;
                        energy += (double)sample * sample;
                    }

                    if (energy <= double.Epsilon)
                        throw new InvalidDataException(
                            "AAC noise source produced a zero-energy scalefactor band.");

                    double normalisation = gain / Math.Sqrt(energy);
                    for (int i = 0; i < bandWidth; i++)
                        coefficients[offset + i] =
                            (float)(coefficients[offset + i] * normalisation);
                }
            }

            result.Add(band with { Coefficients = coefficients });
        }

        return result.AsReadOnly();
    }
}
