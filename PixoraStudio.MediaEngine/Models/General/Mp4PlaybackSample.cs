namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents an encoded sample ready to be passed to a decoder,
/// together with its presentation timestamp.
/// </summary>
public sealed record Mp4PlaybackSample(
    uint TrackId,
    Mp4HandlerType HandlerType,
    double PresentationTimeSeconds,
    Mp4EncodedSample Sample);