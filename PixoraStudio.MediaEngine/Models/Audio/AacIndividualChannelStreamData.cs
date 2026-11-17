namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Parsed syntax elements for one AAC individual_channel_stream.
/// Spectral coefficients remain quantised; synthesis and tool application
/// are performed by later decoder stages.
/// </summary>
public sealed class AacIndividualChannelStreamData
{
    public AacIndividualChannelStreamData(
        int globalGain,
        AacIndividualChannelStreamInfo streamInfo,
        IReadOnlyList<AacSectionInfo> sections,
        AacScaleFactorData scaleFactors,
        AacPulseData? pulseData,
        AacTnsData tnsData,
        IReadOnlyList<AacSpectralBandData> spectralBands)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(globalGain);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(globalGain, 255);
        ArgumentNullException.ThrowIfNull(streamInfo);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(scaleFactors);
        ArgumentNullException.ThrowIfNull(tnsData);
        ArgumentNullException.ThrowIfNull(spectralBands);

        GlobalGain = globalGain;
        StreamInfo = streamInfo;
        Sections = Array.AsReadOnly(sections.ToArray());
        ScaleFactors = scaleFactors;
        PulseData = pulseData;
        TnsData = tnsData;
        SpectralBands = Array.AsReadOnly(spectralBands.ToArray());
    }

    public int GlobalGain { get; }
    public AacIndividualChannelStreamInfo StreamInfo { get; }
    public IReadOnlyList<AacSectionInfo> Sections { get; }
    public AacScaleFactorData ScaleFactors { get; }
    public AacPulseData? PulseData { get; }
    public AacTnsData TnsData { get; }
    public IReadOnlyList<AacSpectralBandData> SpectralBands { get; }
}