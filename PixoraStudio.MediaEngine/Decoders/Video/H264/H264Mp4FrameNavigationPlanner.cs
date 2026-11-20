using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Direction used when navigating to an adjacent presentation frame.
/// </summary>
public enum H264Mp4FrameNavigationDirection
{
    Previous,
    Next
}

/// <summary>
/// Identifies an exact frame in the MP4 presentation timeline.
/// </summary>
public sealed record H264Mp4FrameNavigationTarget(
    uint SampleNumber,
    long PresentationTimeTicks,
    uint Timescale)
{
    public double PresentationTimeSeconds =>
        (double)PresentationTimeTicks / Timescale;
}

/// <summary>
/// Finds adjacent frames using MP4 presentation timestamps rather than
/// assuming a constant frame rate or incrementing by a nominal frame duration.
/// </summary>
public sealed class H264Mp4FrameNavigationPlanner
{
    public H264Mp4FrameNavigationTarget? FindAdjacentFrame(
        Mp4MediaTrack track,
        double currentPresentationTimeSeconds,
        H264Mp4FrameNavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (track.HandlerType != Mp4HandlerType.Video)
            throw new ArgumentException("The selected track is not a video track.", nameof(track));
        if (track.Timescale == 0)
            throw new FormatException("The MP4 video track timescale must be greater than zero.");
        if (!double.IsFinite(currentPresentationTimeSeconds) ||
            currentPresentationTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(currentPresentationTimeSeconds));
        if (direction is not (H264Mp4FrameNavigationDirection.Previous or
            H264Mp4FrameNavigationDirection.Next))
            throw new ArgumentOutOfRangeException(nameof(direction));

        Mp4SampleLocation[] samples = track.SampleLocations.Samples.ToArray();
        if ((uint)samples.Length != track.SampleCount)
            throw new FormatException("The MP4 video sample-location count is inconsistent.");
        if (samples.Length == 0)
            throw new FormatException("The MP4 video track contains no samples.");

        long currentTicks;
        try
        {
            currentTicks = checked((long)decimal.Round(
                (decimal)currentPresentationTimeSeconds * track.Timescale,
                0,
                MidpointRounding.AwayFromZero));
        }
        catch (OverflowException exception)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentPresentationTimeSeconds),
                currentPresentationTimeSeconds,
                $"The requested navigation time exceeds the MP4 track clock range: {exception.Message}");
        }

        List<Mp4SampleLocation> presentationOrder = new(samples.Length);
        for (int index = 0; index < samples.Length; index++)
        {
            Mp4SampleLocation sample = samples[index];
            if (sample.SampleNumber != checked((uint)index + 1) ||
                sample.DurationTicks == 0)
            {
                throw new FormatException(
                    "The MP4 video track contains invalid or non-contiguous sample timing.");
            }

            // Accessing PTS validates the signed composition-time calculation.
            _ = sample.PresentationTimeTicks;
            presentationOrder.Add(sample);
        }

        IEnumerable<Mp4SampleLocation> candidates = direction switch
        {
            H264Mp4FrameNavigationDirection.Previous =>
                presentationOrder.Where(sample => sample.PresentationTimeTicks < currentTicks)
                    .OrderByDescending(sample => sample.PresentationTimeTicks)
                    .ThenByDescending(sample => sample.SampleNumber),
            H264Mp4FrameNavigationDirection.Next =>
                presentationOrder.Where(sample => sample.PresentationTimeTicks > currentTicks)
                    .OrderBy(sample => sample.PresentationTimeTicks)
                    .ThenBy(sample => sample.SampleNumber),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

        Mp4SampleLocation? selected = candidates.FirstOrDefault();
        return selected is null
            ? null
            : new H264Mp4FrameNavigationTarget(
                selected.SampleNumber,
                selected.PresentationTimeTicks,
                track.Timescale);
    }
}
