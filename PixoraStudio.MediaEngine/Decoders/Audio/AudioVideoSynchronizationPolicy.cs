namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Applies a dead-band to independently-clocked audio/video playback.
/// Small drift is expected from device buffering and should not trigger an
/// expensive AAC decoder restart.
/// </summary>
public static class AudioVideoSynchronizationPolicy
{
    public static readonly TimeSpan DefaultCorrectionThreshold =
        TimeSpan.FromMilliseconds(250);

    public static bool RequiresCorrection(
        TimeSpan audioPosition,
        TimeSpan videoPosition,
        TimeSpan? correctionThreshold = null)
    {
        TimeSpan threshold = correctionThreshold ??
            DefaultCorrectionThreshold;

        if (threshold < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(correctionThreshold));

        // Compare tick values as doubles so the absolute difference cannot
        // overflow TimeSpan when callers supply extreme values.
        return Math.Abs((double)audioPosition.Ticks - videoPosition.Ticks) >=
            threshold.Ticks;
    }
}
