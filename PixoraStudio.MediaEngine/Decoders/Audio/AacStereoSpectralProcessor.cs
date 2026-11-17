using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Applies AAC mid/side and intensity stereo reconstruction to a decoded
/// channel pair before IMDCT synthesis.
/// </summary>
public sealed class AacStereoSpectralProcessor
{
    public void Apply(
        Span<float> leftSpectrum,
        Span<float> rightSpectrum,
        AacIndividualChannelStreamData rightChannel,
        IReadOnlyList<int> scalefactorBandOffsets,
        AacIndividualChannelStreamInfo streamInfo,
        ReadOnlySpan<bool> msMask)
    {
        ArgumentNullException.ThrowIfNull(rightChannel);
        ArgumentNullException.ThrowIfNull(scalefactorBandOffsets);
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (leftSpectrum.Length != 1024 || rightSpectrum.Length != 1024 || msMask.Length != 1024)
            throw new ArgumentException("Stereo spectra and mask must each contain 1024 lines.");
        if (scalefactorBandOffsets.Count < streamInfo.MaxScaleFactorBands + 1)
            throw new ArgumentException("Scalefactor-band offsets do not cover max_sfb.");

        Dictionary<(int Group, int Band), AacScaleFactorBandValue> intensity =
            rightChannel.ScaleFactors.Bands
                .Where(value => value.CodebookIndex is 14 or 15)
                .ToDictionary(value => (value.WindowGroup, value.ScaleFactorBand));

        int windowLength = streamInfo.UsesShortWindows ? 128 : 1024;
        int firstWindow = 0;

        for (int group = 0; group < streamInfo.WindowGroupCount; group++)
        {
            for (int band = 0; band < streamInfo.MaxScaleFactorBands; band++)
            {
                int start = scalefactorBandOffsets[band];
                int width = scalefactorBandOffsets[band + 1] - start;
                bool isIntensity = intensity.TryGetValue((group, band), out AacScaleFactorBandValue? position);

                for (int window = 0; window < streamInfo.WindowGroupLengths[group]; window++)
                {
                    int offset = ((firstWindow + window) * windowLength) + start;

                    for (int line = 0; line < width; line++)
                    {
                        int index = offset + line;
                        if (isIntensity)
                        {
                            double gain = Math.Pow(2.0, -position!.Value / 4.0);
                            bool negative = position.CodebookIndex == 15;
                            if (msMask[index])
                                negative = !negative;

                            rightSpectrum[index] = (float)(
                                leftSpectrum[index] * gain * (negative ? -1.0 : 1.0));
                        }
                        else if (msMask[index])
                        {
                            float mid = leftSpectrum[index];
                            float side = rightSpectrum[index];
                            leftSpectrum[index] = mid + side;
                            rightSpectrum[index] = mid - side;
                        }
                    }
                }
            }

            firstWindow += streamInfo.WindowGroupLengths[group];
        }
    }
}
