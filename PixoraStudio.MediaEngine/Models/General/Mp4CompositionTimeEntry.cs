namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents a composition time offset applying to a group of samples.
/// </summary>
public sealed record Mp4CompositionTimeEntry(
    uint SampleCount,
    int SampleOffset);
