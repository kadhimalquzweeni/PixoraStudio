using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VideoThumbnailPlanTests
{
    [Fact]
    public void CreatePositions_ShouldCreateTwelveEvenlyDistributedTargetsByDefault()
    {
        IReadOnlyList<TimeSpan> positions =
            VideoThumbnailPlan.CreatePositions(TimeSpan.FromSeconds(120));

        Assert.Equal(12, positions.Count);
        Assert.Equal(TimeSpan.Zero, positions[0]);
        Assert.Equal(TimeSpan.FromSeconds(10), positions[1]);
        Assert.Equal(TimeSpan.FromSeconds(110), positions[^1]);
    }

    [Fact]
    public void CreatePositions_ShouldReturnEmptyForZeroDuration()
    {
        Assert.Empty(VideoThumbnailPlan.CreatePositions(TimeSpan.Zero));
    }

    [Fact]
    public void CreatePositions_ShouldReturnEmptyWhenCountIsZero()
    {
        Assert.Empty(VideoThumbnailPlan.CreatePositions(
            TimeSpan.FromSeconds(10), 0));
    }

    [Fact]
    public void CreatePositions_ShouldRejectNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoThumbnailPlan.CreatePositions(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void CreatePositions_ShouldRejectNegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoThumbnailPlan.CreatePositions(TimeSpan.FromSeconds(10), -1));
    }
}
