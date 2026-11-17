using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Applies decoded AAC temporal-noise-shaping all-pole filters to a complete
/// 1024-line spectral frame. Short-window spectra are laid out as eight
/// consecutive 128-line windows.
/// </summary>
public sealed class AacTnsSpectralProcessor
{
    private readonly AacTnsCoefficientDecoder _coefficientDecoder = new();

    public float[] Apply(
        ReadOnlySpan<float> spectrum,
        AacTnsData tnsData,
        IReadOnlyList<int> scaleFactorBandOffsets,
        AacIndividualChannelStreamInfo streamInfo)
    {
        ArgumentNullException.ThrowIfNull(tnsData);
        ArgumentNullException.ThrowIfNull(scaleFactorBandOffsets);
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (spectrum.Length != 1024)
            throw new ArgumentException(
                "AAC TNS processing requires exactly 1024 spectral coefficients.",
                nameof(spectrum));

        int windowLength = streamInfo.UsesShortWindows ? 128 : 1024;
        int expectedWindows = streamInfo.UsesShortWindows ? 8 : 1;
        if (tnsData.Windows.Count != expectedWindows)
            throw new InvalidDataException(
                $"TNS data must contain {expectedWindows} window record(s).");

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

        float[] result = spectrum.ToArray();
        int maximumTnsBand = Math.Min(
            streamInfo.MaxScaleFactorBands,
            scaleFactorBandOffsets.Count - 1);

        for (int windowIndex = 0; windowIndex < expectedWindows; windowIndex++)
        {
            AacTnsWindow window = tnsData.Windows[windowIndex];
            int topBand = scaleFactorBandOffsets.Count - 1;

            foreach (AacTnsFilter filter in window.Filters)
            {
                ArgumentNullException.ThrowIfNull(filter);
                if (filter.Length < 0)
                    throw new InvalidDataException("AAC TNS filter length cannot be negative.");

                int bottomBand = Math.Max(0, topBand - filter.Length);
                int startBand = Math.Min(bottomBand, maximumTnsBand);
                int endBand = Math.Min(topBand, maximumTnsBand);
                topBand = bottomBand;

                if (filter.Order == 0 || endBand <= startBand)
                    continue;

                float[] lpc = _coefficientDecoder.Decode(window, filter);
                int start = scaleFactorBandOffsets[startBand];
                int end = scaleFactorBandOffsets[endBand];
                int increment = filter.IsDirectionReversed ? -1 : 1;
                if (filter.IsDirectionReversed)
                    start = end - 1;

                start += windowIndex * windowLength;
                for (int position = 0; position < end - scaleFactorBandOffsets[startBand]; position++)
                {
                    int current = start + (position * increment);
                    int historyCount = Math.Min(position, lpc.Length);

                    for (int i = 1; i <= historyCount; i++)
                        result[current] -= result[current - (i * increment)] * lpc[i - 1];
                }
            }
        }

        return result;
    }
}
