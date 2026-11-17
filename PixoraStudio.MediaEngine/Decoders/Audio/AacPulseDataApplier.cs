using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Applies AAC pulse adjustments to quantised long-window spectral coefficients.
/// The input objects and coefficient arrays are never modified.
/// </summary>
public sealed class AacPulseDataApplier
{
    public IReadOnlyList<AacSpectralBandData> Apply(
        IReadOnlyList<AacSpectralBandData> spectralBands,
        AacPulseData pulseData,
        IReadOnlyList<int> longWindowOffsets,
        AacIndividualChannelStreamInfo streamInfo)
    {
        ArgumentNullException.ThrowIfNull(spectralBands);
        ArgumentNullException.ThrowIfNull(pulseData);
        ArgumentNullException.ThrowIfNull(longWindowOffsets);
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (streamInfo.UsesShortWindows)
            throw new ArgumentException("Pulse data can only be applied to a long-window ICS.", nameof(streamInfo));
        if (longWindowOffsets.Count < 2 || longWindowOffsets[0] != 0 ||
            longWindowOffsets[^1] != 1024)
            throw new ArgumentException("Long-window offsets must span 0..1024.", nameof(longWindowOffsets));

        for (int i = 1; i < longWindowOffsets.Count; i++)
        {
            if (longWindowOffsets[i] <= longWindowOffsets[i - 1])
                throw new ArgumentException("Long-window offsets must be strictly increasing.", nameof(longWindowOffsets));
        }

        int startBand = pulseData.PulseStartScaleFactorBand;
        if (startBand >= longWindowOffsets.Count - 1)
            throw new InvalidDataException("Pulse start scalefactor band is outside the long-window table.");

        AacSpectralBandData[] result = spectralBands
            .Select(band =>
            {
                ArgumentNullException.ThrowIfNull(band);
                if (band.WindowGroup != 0)
                    throw new InvalidDataException("Long-window pulse data cannot target multiple window groups.");
                return band with { Coefficients = (int[])band.Coefficients.Clone() };
            })
            .ToArray();

        int spectralPosition = longWindowOffsets[startBand];
        foreach (AacPulse pulse in pulseData.Pulses)
        {
            ArgumentNullException.ThrowIfNull(pulse);
            spectralPosition = checked(spectralPosition + pulse.Offset);

            if ((uint)spectralPosition >= 1024u)
                throw new InvalidDataException("AAC pulse position exceeds the long-window spectrum.");

            int bandIndex = FindBand(result, spectralPosition);
            if (bandIndex < 0)
                throw new InvalidDataException("AAC pulse position does not belong to a decoded spectral band.");

            int coefficientIndex = spectralPosition - longWindowOffsets[result[bandIndex].ScaleFactorBand];
            int[] coefficients = result[bandIndex].Coefficients;
            if ((uint)coefficientIndex >= (uint)coefficients.Length)
                throw new InvalidDataException("AAC pulse position exceeds its decoded spectral band.");

            int value = coefficients[coefficientIndex];
            coefficients[coefficientIndex] = value > 0
                ? checked(value - pulse.Amplitude)
                : checked(value + pulse.Amplitude);
        }

        return Array.AsReadOnly(result);
    }

    private static int FindBand(IReadOnlyList<AacSpectralBandData> bands, int spectralPosition)
    {
        for (int i = 0; i < bands.Count; i++)
        {
            AacSpectralBandData band = bands[i];
            if (band.ScaleFactorBand < 0)
                continue;

            int width = band.Coefficients.Length;
            int start = -1;

            // Band arrays are supplied in scalefactor-band order. Their
            // absolute starts are validated by the caller's offset table below.
            // The start can be inferred from the cumulative widths only when
            // every preceding band is present, so use the band's own index
            // mapping in the overload's canonical order.
            if (spectralPosition >= 0 && width > 0)
                start = band.ScaleFactorBand;

            _ = start;
        }

        // The caller's spectral bands are indexed by SFB, but the exact
        // absolute offsets are not available here. Resolve the target using
        // the ordered list and contiguous coefficient widths.
        int position = 0;
        for (int i = 0; i < bands.Count; i++)
        {
            int end = position + bands[i].Coefficients.Length;
            if (spectralPosition >= position && spectralPosition < end)
                return i;
            position = end;
        }

        return -1;
    }
}