using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VideoThumbnailPlanTests
{
    [Fact]
    public void TargetThumbnailInterval_ShouldBeTenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), VideoThumbnailPlan.TargetThumbnailInterval);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(60, 6)]
    [InlineData(120, 12)]
    [InlineData(300, 30)]
    [InlineData(305, 32)]
    [InlineData(600, 60)]
    [InlineData(3600, 360)]
    public void GetRecommendedThumbnailCount_ShouldUseTenSecondIntervalsAndIncludePartialEnd(
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
    public void CreatePositions_ShouldCreateThirtyTargetsTenSecondsApartForFiveMinutes()
    {
        IReadOnlyList<TimeSpan> positions =
            VideoThumbnailPlan.CreatePositions(TimeSpan.FromMinutes(5));

        Assert.Equal(30, positions.Count);
        Assert.Equal(TimeSpan.Zero, positions[0]);
        Assert.Equal(TimeSpan.FromSeconds(10), positions[1]);
        Assert.Equal(TimeSpan.FromSeconds(290), positions[^1]);

        for (int index = 1; index < positions.Count; index++)
        {
            Assert.Equal(
                TimeSpan.FromSeconds(10),
                positions[index] - positions[index - 1]);
        }
    }

    [Fact]
    public void CreatePositions_ShouldAppendFinalFrameWhenDurationHasPartialInterval()
    {
        TimeSpan duration = TimeSpan.FromSeconds(305);

        IReadOnlyList<TimeSpan> positions = VideoThumbnailPlan.CreatePositions(duration);

        Assert.Equal(32, positions.Count);
        Assert.Equal(TimeSpan.FromSeconds(300), positions[^2]);
        Assert.Equal(duration - TimeSpan.FromTicks(1), positions[^1]);
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
