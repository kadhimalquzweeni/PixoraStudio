using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VideoThumbnailPlanTests
{
    [Fact]
    public void CreatePositions_ShouldCreateTwelveEvenlyDistributedTargetsForTwoMinutes()
    {
        IReadOnlyList<TimeSpan> positions =
            VideoThumbnailPlan.CreatePositions(TimeSpan.FromSeconds(120));

        Assert.Equal(12, positions.Count);
        Assert.Equal(TimeSpan.Zero, positions[0]);
        Assert.Equal(TimeSpan.FromSeconds(10), positions[1]);
        Assert.Equal(TimeSpan.FromSeconds(110), positions[^1]);
    }

    [Theory]
    [InlineData(10, 12)]
    [InlineData(60, 12)]
    [InlineData(240, 12)]
    [InlineData(300, 15)]
    [InlineData(600, 30)]
    [InlineData(1200, 48)]
    [InlineData(3600, 48)]
    public void GetRecommendedThumbnailCount_ShouldScaleWithDurationAndRespectLimits(
        int durationSeconds,
        int expectedCount)
    {
        Assert.Equal(
            expectedCount,
            VideoThumbnailPlan.GetRecommendedThumbnailCount(
                TimeSpan.FromSeconds(durationSeconds)));
    }

    [Fact]
    public void GetRecommendedThumbnailCount_ShouldReturnZeroForZeroDuration()
    {
        Assert.Equal(0, VideoThumbnailPlan.GetRecommendedThumbnailCount(TimeSpan.Zero));
    }

    [Fact]
    public void GetRecommendedThumbnailCount_ShouldRejectNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoThumbnailPlan.GetRecommendedThumbnailCount(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void CreatePositions_ShouldUseAdaptiveCountAndRemainEvenlyDistributed()
    {
        TimeSpan duration = TimeSpan.FromMinutes(10);

        IReadOnlyList<TimeSpan> positions = VideoThumbnailPlan.CreatePositions(duration);

        Assert.Equal(30, positions.Count);
        Assert.Equal(TimeSpan.Zero, positions[0]);
        Assert.Equal(TimeSpan.FromSeconds(20), positions[1]);
        Assert.Equal(TimeSpan.FromSeconds(580), positions[^1]);
        Assert.All(positions, position =>
            Assert.InRange(position, TimeSpan.Zero, duration - TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void CreatePositions_ShouldReturnEmptyForZeroDuration()
    {
        Assert.Empty(VideoThumbnailPlan.CreatePositions(TimeSpan.Zero));
    }

    [Fact]
    public void CreatePositions_WithExplicitZeroCount_ShouldReturnEmpty()
    {
        Assert.Empty(VideoThumbnailPlan.CreatePositions(
            TimeSpan.FromSeconds(10), 0));
    }

    [Fact]
    public void CreatePositions_WithExplicitCount_ShouldPreserveCallerChoice()
    {
        IReadOnlyList<TimeSpan> positions = VideoThumbnailPlan.CreatePositions(
            TimeSpan.FromSeconds(60), 5);

        Assert.Equal(5, positions.Count);
        Assert.Equal(TimeSpan.FromSeconds(12), positions[1]);
        Assert.Equal(TimeSpan.FromSeconds(48), positions[^1]);
    }

    [Fact]
    public void CreatePositions_ShouldRejectNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoThumbnailPlan.CreatePositions(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void CreatePositions_WithExplicitCount_ShouldRejectNegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoThumbnailPlan.CreatePositions(TimeSpan.FromSeconds(10), -1));
    }
}
