using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Runs the AAC-LC spectral reconstruction stages for one parsed ICS and
/// returns its 1024-line frequency-domain frame. Stereo tools and IMDCT
/// synthesis are intentionally handled by later stages.
/// </summary>
public sealed class AacIndividualChannelStreamProcessor
{
    private readonly AacPulseDataApplier _pulseApplier = new();
    private readonly AacSpectralDequantizer _dequantizer = new();
    private readonly AacPnsNoiseGenerator _pnsGenerator;
    private readonly AacTnsSpectralProcessor _tnsProcessor = new();

    public AacIndividualChannelStreamProcessor(IAacNoiseSource? noiseSource = null)
    {
        _pnsGenerator = new AacPnsNoiseGenerator(noiseSource);
    }

    public float[] Process(
        AacIndividualChannelStreamData streamData,
        int samplingFrequencyIndex)
    {
        ArgumentNullException.ThrowIfNull(streamData);

        IReadOnlyList<int> offsets = streamData.StreamInfo.UsesShortWindows
            ? AacScaleFactorBandTable.GetShortWindowOffsets(samplingFrequencyIndex)
            : AacScaleFactorBandTable.GetLongWindowOffsets(samplingFrequencyIndex);

        IReadOnlyList<AacSpectralBandData> quantisedBands = streamData.SpectralBands;
        if (streamData.PulseData is not null)
        {
            quantisedBands = _pulseApplier.Apply(
                quantisedBands,
                streamData.PulseData,
                AacScaleFactorBandTable.GetLongWindowOffsets(samplingFrequencyIndex),
                streamData.StreamInfo);
        }

        IReadOnlyList<AacDequantizedSpectralBandData> dequantized =
            _dequantizer.Dequantize(quantisedBands, streamData.ScaleFactors);

        dequantized = _pnsGenerator.Apply(
            dequantized,
            streamData.ScaleFactors,
            offsets,
            streamData.StreamInfo);

        float[] spectrum = ReassembleSpectrum(
            dequantized,
            offsets,
            streamData.StreamInfo);

        return _tnsProcessor.Apply(
            spectrum,
            streamData.TnsData,
            offsets,
            streamData.StreamInfo);
    }

    private static float[] ReassembleSpectrum(
        IReadOnlyList<AacDequantizedSpectralBandData> bands,
        IReadOnlyList<int> offsets,
        AacIndividualChannelStreamInfo streamInfo)
    {
        int windowLength = streamInfo.UsesShortWindows ? 128 : 1024;
        IReadOnlyList<int> groupLengths = streamInfo.WindowGroupLengths;
        float[] spectrum = new float[1024];
        HashSet<(int Group, int Band)> populated = [];

        foreach (AacDequantizedSpectralBandData band in bands)
        {
            ArgumentNullException.ThrowIfNull(band);

            if ((uint)band.WindowGroup >= (uint)groupLengths.Count ||
                (uint)band.ScaleFactorBand >= (uint)(offsets.Count - 1))
            {
                throw new InvalidDataException(
                    "Decoded spectral band references an invalid window group or scalefactor band.");
            }

            if (!populated.Add((band.WindowGroup, band.ScaleFactorBand)))
                throw new InvalidDataException(
                    "Decoded ICS contains duplicate spectral data for a scalefactor band.");

            int bandStart = offsets[band.ScaleFactorBand];
            int bandWidth = offsets[band.ScaleFactorBand + 1] - bandStart;
            int groupLength = groupLengths[band.WindowGroup];
            if (band.Coefficients.Length != checked(bandWidth * groupLength))
                throw new InvalidDataException(
                    "Decoded spectral coefficient count does not match its grouped scalefactor-band width.");

            int firstWindow = 0;
            for (int group = 0; group < band.WindowGroup; group++)
                firstWindow += groupLengths[group];

            for (int windowInGroup = 0; windowInGroup < groupLength; windowInGroup++)
            {
                int sourceOffset = windowInGroup * bandWidth;
                int destinationOffset =
                    ((firstWindow + windowInGroup) * windowLength) + bandStart;

                Array.Copy(
                    band.Coefficients,
                    sourceOffset,
                    spectrum,
                    destinationOffset,
                    bandWidth);
            }
        }

        return spectrum;
    }
}
