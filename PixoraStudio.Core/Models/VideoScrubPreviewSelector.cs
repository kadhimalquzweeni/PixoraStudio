namespace PixoraStudio.Core.Models;

/// <summary>Selects the filmstrip thumbnail closest to a requested playback position.</summary>
public static class VideoScrubPreviewSelector
{
    /// <summary>Returns the closest thumbnail index, or -1 when inputs are unusable.</summary>
    public static int FindNearestThumbnailIndex(
        double positionMilliseconds,
        double totalMilliseconds,
        int thumbnailCount)
    {
        if (thumbnailCount <= 0 ||
            !double.IsFinite(positionMilliseconds) ||
            !double.IsFinite(totalMilliseconds) ||
            totalMilliseconds <= 0)
            return -1;

        IReadOnlyList<TimeSpan> positions = VideoThumbnailPlan.CreatePositions(
            TimeSpan.FromMilliseconds(totalMilliseconds), thumbnailCount);
        if (positions.Count == 0)
            return -1;

        TimeSpan target = TimeSpan.FromMilliseconds(
            Math.Clamp(positionMilliseconds, 0, totalMilliseconds));
        int nearestIndex = 0;
        long nearestDistance = Math.Abs((positions[0] - target).Ticks);

        for (int index = 1; index < positions.Count; index++)
        {
            long distance = Math.Abs((positions[index] - target).Ticks);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }
}
