namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Represents one AAC channel-pair element and its per-spectral-line MS mask.
/// </summary>
public sealed record AacChannelPairElement(
    byte ElementInstanceTag,
    bool CommonWindow,
    AacIndividualChannelStreamData LeftChannel,
    AacIndividualChannelStreamData RightChannel,
    bool[] MidSideMask);
