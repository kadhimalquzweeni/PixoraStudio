namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// A decoded AAC sample positioned on the presentation timeline relative to
/// the requested seek target.
/// </summary>
public sealed record Mp4DecodedAudioSample(
    uint SampleNumber,
    long PresentationStartFrame,
    PcmAudioBuffer Buffer);
