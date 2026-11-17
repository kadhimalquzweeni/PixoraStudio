namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Represents one supported AAC single-channel element from a raw_data_block.
/// </summary>
public sealed record AacSingleChannelElement(
    byte ElementInstanceTag,
    AacIndividualChannelStreamData ChannelStream);
