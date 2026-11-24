namespace PixoraStudio.MediaEngine.Processing.Timeline;

/// <summary>
/// Converts absolute MP4 presentation timestamps into PCM frame positions.
/// Each timestamp is converted independently to avoid cumulative rounding drift.
/// </summary>
public static class Mp4AudioTimelineFrameConverter
{
    public static long ToSampleFrame(
        long presentationTimeTicks,
        uint trackTimescale,
        int sampleRate)
    {
        if (trackTimescale == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trackTimescale));
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        decimal framePosition =
            ((decimal)presentationTimeTicks * sampleRate) / trackTimescale;

        return checked((long)decimal.Round(
            framePosition,
            0,
            MidpointRounding.AwayFromZero));
    }
}
