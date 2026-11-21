namespace PixoraStudio.Core.Models;

/// <summary>Creates filmstrip seek targets with enough frames to fill the timeline.</summary>
public static class VideoThumbnailPlan
{
    /// <summary>Target spacing between consecutive filmstrip thumbnails for longer videos.</summary>
    public static readonly TimeSpan TargetThumbnailInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Minimum number of thumbnails used to populate the timeline, including for
    /// short videos whose ten-second sampling plan would otherwise leave it sparse.
    /// </summary>
    public const int MinimumThumbnailCount = 10;

    /// <summary>
    /// Returns the greater of the minimum filmstrip count and the regular
    /// ten-second sampling count (including a final partial-interval position).
    /// </summary>
    public static int GetRecommendedThumbnailCount(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        if (duration == TimeSpan.Zero)
            return 0;

        long intervalCount = 1 + ((duration.Ticks - 1) / TargetThumbnailInterval.Ticks);
        bool hasPartialInterval =
            duration > TargetThumbnailInterval &&
            duration.Ticks % TargetThumbnailInterval.Ticks != 0;

        int intervalBasedCount = checked((int)(
            intervalCount + (hasPartialInterval ? 1 : 0)));

        return Math.Max(MinimumThumbnailCount, intervalBasedCount);
    }

    /// <summary>
    /// Creates evenly distributed positions when the regular ten-second plan
    /// would produce fewer than the minimum number of timeline thumbnails.
    /// Longer videos retain the existing ten-second sampling and final-frame
    /// behaviour.
    /// </summary>
    public static IReadOnlyList<TimeSpan> CreatePositions(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        if (duration == TimeSpan.Zero)
            return Array.Empty<TimeSpan>();

        int count = GetRecommendedThumbnailCount(duration);
        long intervalCount = 1 + ((duration.Ticks - 1) / TargetThumbnailInterval.Ticks);
        bool hasPartialInterval =
            duration > TargetThumbnailInterval &&
            duration.Ticks % TargetThumbnailInterval.Ticks != 0;
        int intervalBasedCount = checked((int)(
            intervalCount + (hasPartialInterval ? 1 : 0)));

        if (count > intervalBasedCount)
            return CreatePositions(duration, count);

        var positions = new List<TimeSpan>(count);

        for (long ticks = 0; ticks < duration.Ticks; ticks += TargetThumbnailInterval.Ticks)
            positions.Add(TimeSpan.FromTicks(ticks));

        if (hasPartialInterval)
            positions.Add(duration - TimeSpan.FromTicks(1));

        return positions;
    }

    /// <summary>Creates evenly distributed positions using an explicit thumbnail count.</summary>
    public static IReadOnlyList<TimeSpan> CreatePositions(TimeSpan duration, int count)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0 || duration == TimeSpan.Zero)
            return Array.Empty<TimeSpan>();

        // Keep each target inside the video and avoid duplicate positions for
        // extremely short durations that contain fewer ticks than requested frames.
        int effectiveCount = (int)Math.Min(count, duration.Ticks);

        return Enumerable.Range(0, effectiveCount)
            .Select(index => TimeSpan.FromTicks(
                (long)((double)duration.Ticks * index / effectiveCount)))
            .ToArray();
    }
}
