namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Describes a sample's position on the presentation timeline.
/// </summary>
public sealed record Mp4PresentationSample(
    uint TrackId,
    uint SampleNumber,
    Mp4SampleLocation Location,
    double PresentationTimeSeconds);