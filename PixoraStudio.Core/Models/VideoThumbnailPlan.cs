namespace PixoraStudio.Core.Models;

/// <summary>Creates evenly distributed seek targets for a video filmstrip.</summary>
public static class VideoThumbnailPlan
{
    /// <summary>Number of representative frames used by the editor filmstrip.</summary>
    public const int DefaultThumbnailCount = 12;

    /// <summary>Bounds concurrent decoding to avoid saturating CPU and memory.</summary>
    public const int MaxConcurrentGenerations = 4;

    public static IReadOnlyList<TimeSpan> CreatePositions(
        TimeSpan duration,
        int count = DefaultThumbnailCount)
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
