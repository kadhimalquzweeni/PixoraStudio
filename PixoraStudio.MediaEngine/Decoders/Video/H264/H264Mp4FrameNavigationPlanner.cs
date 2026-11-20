using System.Runtime.CompilerServices;
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
    private readonly ConditionalWeakTable<Mp4MediaTrack, NavigationIndex> _indices = new();

    public H264Mp4FrameNavigationTarget? FindAdjacentFrame(
        Mp4MediaTrack track,
        double currentPresentationTimeSeconds,
        H264Mp4FrameNavigationDirection direction)
    {
        ValidateTrack(track, direction);
        if (!double.IsFinite(currentPresentationTimeSeconds) ||
            currentPresentationTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(currentPresentationTimeSeconds));

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

        return FindAdjacentFrameCore(track, currentTicks, null, direction);
    }

    /// <summary>
    /// Finds an adjacent frame relative to a specific sample identity. This
    /// overload also navigates correctly when multiple samples share a PTS.
    /// </summary>
    public H264Mp4FrameNavigationTarget? FindAdjacentFrame(
        Mp4MediaTrack track,
        H264Mp4FrameNavigationTarget currentFrame,
        H264Mp4FrameNavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(currentFrame);
        ValidateTrack(track, direction);

        if (currentFrame.Timescale != track.Timescale ||
            currentFrame.SampleNumber == 0 ||
            currentFrame.SampleNumber > track.SampleCount)
            throw new ArgumentException(
                "The current frame does not belong to the selected MP4 track.",
                nameof(currentFrame));

        Mp4SampleLocation currentSample = track.SampleLocations.Samples[
            checked((int)currentFrame.SampleNumber - 1)];
        if (currentSample.SampleNumber != currentFrame.SampleNumber ||
            currentSample.PresentationTimeTicks != currentFrame.PresentationTimeTicks)
            throw new ArgumentException(
                "The current frame timestamp does not match its MP4 sample.",
                nameof(currentFrame));

        return FindAdjacentFrameCore(
            track,
            currentFrame.PresentationTimeTicks,
            currentFrame.SampleNumber,
            direction);
    }

    private static void ValidateTrack(
        Mp4MediaTrack track,
        H264Mp4FrameNavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.HandlerType != Mp4HandlerType.Video)
            throw new ArgumentException("The selected track is not a video track.", nameof(track));
        if (track.Timescale == 0)
            throw new FormatException("The MP4 video track timescale must be greater than zero.");
        if (direction is not (H264Mp4FrameNavigationDirection.Previous or
            H264Mp4FrameNavigationDirection.Next))
            throw new ArgumentOutOfRangeException(nameof(direction));
    }

    private H264Mp4FrameNavigationTarget? FindAdjacentFrameCore(
        Mp4MediaTrack track,
        long currentTicks,
        uint? currentSampleNumber,
        H264Mp4FrameNavigationDirection direction)
    {
        ValidateTrack(track, direction);
        Mp4SampleLocation[] presentationOrder =
            _indices.GetValue(track, static mediaTrack => BuildIndex(mediaTrack)).Samples;

        int selectedIndex = direction switch
        {
            H264Mp4FrameNavigationDirection.Previous =>
                LowerBound(
                    presentationOrder,
                    currentTicks,
                    currentSampleNumber ?? 0) - 1,
            H264Mp4FrameNavigationDirection.Next =>
                UpperBound(
                    presentationOrder,
                    currentTicks,
                    currentSampleNumber ?? uint.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

        if (selectedIndex < 0 || selectedIndex >= presentationOrder.Length)
            return null;

        Mp4SampleLocation selected = presentationOrder[selectedIndex];
        return new H264Mp4FrameNavigationTarget(
            selected.SampleNumber,
            selected.PresentationTimeTicks,
            track.Timescale);
    }

    private static NavigationIndex BuildIndex(Mp4MediaTrack track)
    {
        ValidateTrack(track, H264Mp4FrameNavigationDirection.Next);
        Mp4SampleLocation[] samples = track.SampleLocations.Samples.ToArray();
        if ((uint)samples.Length != track.SampleCount)
            throw new FormatException("The MP4 video sample-location count is inconsistent.");
        if (samples.Length == 0)
            throw new FormatException("The MP4 video track contains no samples.");

        for (int index = 0; index < samples.Length; index++)
        {
            Mp4SampleLocation sample = samples[index];
            if (sample.SampleNumber != checked((uint)index + 1) ||
                sample.DurationTicks == 0)
                throw new FormatException(
                    "The MP4 video track contains invalid or non-contiguous sample timing.");

            _ = sample.PresentationTimeTicks;
        }

        return new NavigationIndex(samples
            .OrderBy(sample => sample.PresentationTimeTicks)
            .ThenBy(sample => sample.SampleNumber)
            .ToArray());
    }

    private static int LowerBound(
        Mp4SampleLocation[] samples,
        long presentationTicks,
        uint sampleNumber)
    {
        int low = 0;
        int high = samples.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            Mp4SampleLocation sample = samples[middle];
            bool before = sample.PresentationTimeTicks < presentationTicks ||
                (sample.PresentationTimeTicks == presentationTicks &&
                 sample.SampleNumber < sampleNumber);
            if (before)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int UpperBound(
        Mp4SampleLocation[] samples,
        long presentationTicks,
        uint sampleNumber)
    {
        int low = 0;
        int high = samples.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            Mp4SampleLocation sample = samples[middle];
            bool atOrBefore = sample.PresentationTimeTicks < presentationTicks ||
                (sample.PresentationTimeTicks == presentationTicks &&
                 sample.SampleNumber <= sampleNumber);
            if (atOrBefore)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private sealed record NavigationIndex(Mp4SampleLocation[] Samples);
}
