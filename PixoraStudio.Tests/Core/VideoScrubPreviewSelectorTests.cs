using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VideoScrubPreviewSelectorTests
{
    [Fact]
    public void FindNearestThumbnailIndex_ShouldReturnFirstThumbnailAtStart() =>
        Assert.Equal(0, VideoScrubPreviewSelector.FindNearestThumbnailIndex(0, 60_000, 12));

    [Fact]
    public void FindNearestThumbnailIndex_ShouldReturnLastThumbnailNearEnd() =>
        Assert.Equal(11, VideoScrubPreviewSelector.FindNearestThumbnailIndex(59_999, 60_000, 12));

    [Fact]
    public void FindNearestThumbnailIndex_ShouldSelectClosestEvenlySpacedThumbnail() =>
        Assert.Equal(3, VideoScrubPreviewSelector.FindNearestThumbnailIndex(16_000, 60_000, 12));

    [Fact]
    public void FindNearestThumbnailIndex_ShouldClampPositionsOutsideVideo()
    {
        Assert.Equal(0, VideoScrubPreviewSelector.FindNearestThumbnailIndex(-5_000, 60_000, 12));
        Assert.Equal(11, VideoScrubPreviewSelector.FindNearestThumbnailIndex(80_000, 60_000, 12));
    }

    [Theory]
    [InlineData(double.NaN, 60_000, 12)]
    [InlineData(double.PositiveInfinity, 60_000, 12)]
    [InlineData(0, double.NaN, 12)]
    [InlineData(0, 0, 12)]
    [InlineData(0, -1, 12)]
    [InlineData(0, 60_000, 0)]
    [InlineData(0, 60_000, -1)]
    public void FindNearestThumbnailIndex_ShouldReturnMinusOneForInvalidInputs(
        double position, double duration, int thumbnailCount) =>
        Assert.Equal(-1, VideoScrubPreviewSelector.FindNearestThumbnailIndex(
            position, duration, thumbnailCount));

    [Fact]
    public void FindNearestThumbnailIndex_ShouldPreferEarlierThumbnailOnExactTie() =>
        Assert.Equal(0, VideoScrubPreviewSelector.FindNearestThumbnailIndex(
            2_500, 60_000, 12));
}
