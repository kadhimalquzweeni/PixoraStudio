using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads the complete AAC individual_channel_stream syntax in bitstream order.
/// This reader parses AAC-LC syntax but does not yet apply pulse/TNS/PNS tools
/// or transform quantised coefficients into PCM.
/// </summary>
public sealed class AacIndividualChannelStreamDataReader
{
    private readonly AacIndividualChannelStreamInfoReader _infoReader = new();
    private readonly AacSectionDataReader _sectionReader = new();
    private readonly AacScaleFactorDataReader _scaleFactorReader = new();
    private readonly AacPulseDataReader _pulseReader = new();
    private readonly AacTnsDataReader _tnsReader = new();
    private readonly AacSpectralSectionDecoder _spectralDecoder = new();

    /// <summary>
    /// Reads one ICS. When <paramref name="commonWindow"/> is true,
    /// <paramref name="sharedStreamInfo"/> must contain the ICS info parsed
    /// from the enclosing channel-pair element.
    /// </summary>
    public AacIndividualChannelStreamData Read(
        AacBitReader reader,
        int samplingFrequencyIndex,
        bool commonWindow = false,
        AacIndividualChannelStreamInfo? sharedStreamInfo = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // Validate configuration and shared metadata before consuming the bitstream.
        _ = AacScaleFactorBandTable.GetLongWindowOffsets(samplingFrequencyIndex);
        if (commonWindow && sharedStreamInfo is null)
            throw new ArgumentNullException(
                nameof(sharedStreamInfo),
                "Shared ICS information is required when common_window is true.");

        int globalGain = (int)reader.ReadBits(8);

        AacIndividualChannelStreamInfo streamInfo =
            commonWindow ? sharedStreamInfo! : _infoReader.Read(reader);

        IReadOnlyList<int> offsets = streamInfo.UsesShortWindows
            ? AacScaleFactorBandTable.GetShortWindowOffsets(samplingFrequencyIndex)
            : AacScaleFactorBandTable.GetLongWindowOffsets(samplingFrequencyIndex);

        if (streamInfo.MaxScaleFactorBands > offsets.Count - 1)
            throw new InvalidDataException(
                "AAC max_sfb exceeds the scalefactor-band table for this sampling frequency.");

        IReadOnlyList<AacSectionInfo> sections = _sectionReader.Read(reader, streamInfo);

        AacScaleFactorData scaleFactors =
            _scaleFactorReader.Read(reader, sections, globalGain);

        AacPulseData? pulseData = null;
        if (reader.ReadBits(1) != 0)
            pulseData = _pulseReader.Read(reader, streamInfo);

        AacTnsData tnsData = new(Array.Empty<AacTnsWindow>());
        if (reader.ReadBits(1) != 0)
            tnsData = _tnsReader.Read(reader, streamInfo);

        bool gainControlDataPresent = reader.ReadBits(1) != 0;
        if (gainControlDataPresent)
            throw new NotSupportedException(
                "AAC gain_control_data() is not supported by the AAC-LC decoder.");


        IReadOnlyList<int> activeOffsets = offsets;
        IReadOnlyList<AacSpectralBandData> spectralBands =
            _spectralDecoder.Decode(
                reader,
                sections,
                activeOffsets,
                streamInfo.WindowGroupLengths);


        return new AacIndividualChannelStreamData(
            globalGain,
            streamInfo,
            sections,
            scaleFactors,
            pulseData,
            tnsData,
            spectralBands);
    }
}