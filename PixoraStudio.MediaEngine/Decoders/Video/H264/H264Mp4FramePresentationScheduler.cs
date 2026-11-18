namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

public enum H264FramePresentationAction
{
    Wait,
    Present,
    Drop
}

public readonly record struct H264FramePresentationDecision(
    H264FramePresentationAction Action,
    TimeSpan Delay);

/// <summary>
/// Decides whether a decoded frame is early, due, or too late for the current
/// playback clock. The scheduler deliberately does not own a clock or timer;
/// the host supplies the authoritative playback position.
/// </summary>
public sealed class H264Mp4FramePresentationScheduler
{
    private const double EarlyToleranceSeconds = 0.005;

    public H264FramePresentationDecision Evaluate(
        double framePresentationTimeSeconds,
        double playbackTimeSeconds,
        double maximumLatenessSeconds = 0.080)
    {
        if (!double.IsFinite(framePresentationTimeSeconds))
            throw new ArgumentOutOfRangeException(nameof(framePresentationTimeSeconds));
        if (!double.IsFinite(playbackTimeSeconds) || playbackTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(playbackTimeSeconds));
        if (!double.IsFinite(maximumLatenessSeconds) || maximumLatenessSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLatenessSeconds));

        double delta = framePresentationTimeSeconds - playbackTimeSeconds;
        if (delta > EarlyToleranceSeconds)
        {
            TimeSpan delay = TimeSpan.FromSeconds(Math.Min(delta, 0.025));
            return new H264FramePresentationDecision(H264FramePresentationAction.Wait, delay);
        }

        if (delta < -maximumLatenessSeconds - 1e-9)
            return new H264FramePresentationDecision(H264FramePresentationAction.Drop, TimeSpan.Zero);

        return new H264FramePresentationDecision(H264FramePresentationAction.Present, TimeSpan.Zero);
    }
}
