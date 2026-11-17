using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Applies AAC PNS correlation, mid/side and intensity stereo reconstruction
/// to a decoded channel pair before TNS and IMDCT synthesis.
/// </summary>
public sealed class AacStereoSpectralProcessor
{
    public void Apply(
        Span<float> leftSpectrum,
        Span<float> rightSpectrum,
        AacIndividualChannelStreamData leftChannel,
        AacIndividualChannelStreamData rightChannel,
        IReadOnlyList<int> scalefactorBandOffsets,
        AacIndividualChannelStreamInfo streamInfo,
        ReadOnlySpan<bool> msMask)
    {
        ArgumentNullException.ThrowIfNull(leftChannel);
        ArgumentNullException.ThrowIfNull(rightChannel);
        ArgumentNullException.ThrowIfNull(scalefactorBandOffsets);
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (leftSpectrum.Length != 1024 || rightSpectrum.Length != 1024 || msMask.Length != 1024)
            throw new ArgumentException("Stereo spectra and mask must each contain 1024 lines.");
        if (scalefactorBandOffsets.Count < streamInfo.MaxScaleFactorBands + 1)
            throw new ArgumentException("Scalefactor-band offsets do not cover max_sfb.");

        Dictionary<(int Group, int Band), AacScaleFactorBandValue> leftNoise =
            GetBands(leftChannel, 13);
        Dictionary<(int Group, int Band), AacScaleFactorBandValue> rightNoise =
            GetBands(rightChannel, 13);
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
                bool isIntensity = intensity.TryGetValue(
                    (group, band), out AacScaleFactorBandValue? intensityPosition);
                bool isLeftNoise = leftNoise.TryGetValue(
                    (group, band), out AacScaleFactorBandValue? leftNoiseEnergy);
                bool isRightNoise = rightNoise.TryGetValue(
                    (group, band), out AacScaleFactorBandValue? rightNoiseEnergy);

                for (int window = 0; window < streamInfo.WindowGroupLengths[group]; window++)
                {
                    int offset = ((firstWindow + window) * windowLength) + start;

                    for (int line = 0; line < width; line++)
                    {
                        int index = offset + line;

                        if (isIntensity)
                        {
                            double gain = Math.Pow(2.0, -intensityPosition!.Value / 4.0);
                            // Codebook 15 is in-phase; codebook 14 is out-of-phase.
                            bool negative = intensityPosition!.CodebookIndex == 14;
                            if (msMask[index])
                                negative = !negative;

                            rightSpectrum[index] = (float)(
                                leftSpectrum[index] * gain * (negative ? -1.0 : 1.0));
                        }
                        else if (isLeftNoise || isRightNoise)
                        {
                            // PNS bands are not M/S decoded. When both channels
                            // carry PNS and ms_used is set, reuse the left noise
                            // vector and apply the right channel's energy ratio.
                            if (isLeftNoise && isRightNoise && msMask[index])
                            {
                                double gain = Math.Pow(
                                    2.0,
                                    (rightNoiseEnergy!.Value - leftNoiseEnergy!.Value) / 4.0);
                                rightSpectrum[index] = (float)(leftSpectrum[index] * gain);
                            }
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

    private static Dictionary<(int Group, int Band), AacScaleFactorBandValue> GetBands(
        AacIndividualChannelStreamData channel,
        int codebook) =>
        channel.ScaleFactors.Bands
            .Where(value => value.CodebookIndex == codebook)
            .ToDictionary(value => (value.WindowGroup, value.ScaleFactorBand));
}
