namespace PixoraStudio.Core.Models;

/// <summary>
/// Converts the editor's percentage-based volume setting into the levels
/// required by the PCM and LibVLC playback outputs.
/// </summary>
public readonly record struct AudioOutputSettings(
    float PcmVolume,
    int LibVlcVolume,
    bool IsMuted,
    bool LibVlcMuted)
{
    public static AudioOutputSettings Create(
        double volumePercent,
        bool isMuted,
        bool usesCustomAacAudio)
    {
        double safeVolume = double.IsFinite(volumePercent)
            ? Math.Clamp(volumePercent, 0d, 100d)
            : 0d;

        return new AudioOutputSettings(
            (float)(safeVolume / 100d),
            (int)Math.Round(safeVolume, MidpointRounding.AwayFromZero),
            isMuted,
            usesCustomAacAudio || isMuted);
    }
}
