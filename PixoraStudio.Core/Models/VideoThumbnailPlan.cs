namespace PixoraStudio.Core.Models;

/// <summary>Creates evenly distributed seek targets for a video filmstrip.</summary>
public static class VideoThumbnailPlan
{
    /// <summary>Minimum number of representative frames used by the editor filmstrip.</summary>
    public const int DefaultThumbnailCount = 12;

    /// <summary>Maximum number of frames used to keep long-video filmstrips responsive.</summary>
    public const int MaximumThumbnailCount = 48;

    /// <summary>Target spacing between filmstrip thumbnails before applying the count limits.</summary>
    public static readonly TimeSpan TargetThumbnailInterval = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Chooses a thumbnail count based on video duration. The count grows by
    /// approximately one thumbnail per 20 seconds, with a minimum of 12 and
    /// maximum of 48 to balance visual coverage and generation time.
    /// </summary>
    public static int GetRecommendedThumbnailCount(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        if (duration == TimeSpan.Zero)
            return 0;

        long intervals = 1 + ((duration.Ticks - 1) / TargetThumbnailInterval.Ticks);
        return (int)Math.Clamp(
            intervals,
            DefaultThumbnailCount,
            MaximumThumbnailCount);
    }

    /// <summary>Creates evenly distributed positions using the adaptive thumbnail count.</summary>
    public static IReadOnlyList<TimeSpan> CreatePositions(TimeSpan duration) =>
        CreatePositions(duration, GetRecommendedThumbnailCount(duration));

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
