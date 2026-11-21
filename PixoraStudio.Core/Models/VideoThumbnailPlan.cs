namespace PixoraStudio.Core.Models;

/// <summary>Creates filmstrip seek targets at a fixed time interval.</summary>
public static class VideoThumbnailPlan
{
    /// <summary>Target spacing between consecutive filmstrip thumbnails.</summary>
    public static readonly TimeSpan TargetThumbnailInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Returns the number of regular ten-second positions, plus a final position
    /// when the duration has a partial interval and is longer than one interval.
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

        return checked((int)(intervalCount + (hasPartialInterval ? 1 : 0)));
    }

    /// <summary>
    /// Creates positions at zero and every ten seconds thereafter. If the
    /// duration ends between ten-second boundaries, the final frame is also
    /// included so the end of the video is represented in the filmstrip.
    /// </summary>
    public static IReadOnlyList<TimeSpan> CreatePositions(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        if (duration == TimeSpan.Zero)
            return Array.Empty<TimeSpan>();

        int count = GetRecommendedThumbnailCount(duration);
        var positions = new List<TimeSpan>(count);

        for (long ticks = 0; ticks < duration.Ticks; ticks += TargetThumbnailInterval.Ticks)
            positions.Add(TimeSpan.FromTicks(ticks));

        if (duration > TargetThumbnailInterval &&
            duration.Ticks % TargetThumbnailInterval.Ticks != 0)
        {
            positions.Add(duration - TimeSpan.FromTicks(1));
        }

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

        return Enumerable.Range(0, count)
            .Select(index => TimeSpan.FromTicks(
                (long)((double)duration.Ticks * index / count)))
            .ToArray();
    }
}
