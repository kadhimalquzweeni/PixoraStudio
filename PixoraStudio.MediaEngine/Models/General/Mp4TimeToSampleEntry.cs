namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents a time-to-sample table entry.
/// </summary>
public sealed record Mp4TimeToSampleEntry(
    uint SampleCount,
    uint SampleDelta);
