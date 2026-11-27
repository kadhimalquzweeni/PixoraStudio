using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class AspectRatioDimensionsTests
{
    [Theory]
    [InlineData(1920, 1080, 1280, 1280, 720)]
    [InlineData(1920, 1080, 2000, 2000, 1125)]
    [InlineData(1920, 1080, 1440, 1440, 810)]
    [InlineData(100, 100, 250, 250, 250)]
    [InlineData(1000, 667, 1400, 1000, 667)]
    public void FromWidth_ShouldReturnNearestDimensionsWithExactSourceRatio(
        int sourceWidth, int sourceHeight, int requestedWidth,
        int expectedWidth, int expectedHeight)
    {
        var result = AspectRatioDimensions.FromWidth(sourceWidth, sourceHeight, requestedWidth);

        Assert.Equal((expectedWidth, expectedHeight), result);
        Assert.Equal((long)sourceWidth * result.Height, (long)sourceHeight * result.Width);
    }

    [Theory]
    [InlineData(1920, 1080, 1440, 2560, 1440)]
    [InlineData(1920, 1080, 720, 1280, 720)]
    [InlineData(100, 100, 250, 250, 250)]
    [InlineData(1000, 667, 1300, 2000, 1334)]
    public void FromHeight_ShouldReturnNearestDimensionsWithExactSourceRatio(
        int sourceWidth, int sourceHeight, int requestedHeight,
        int expectedWidth, int expectedHeight)
    {
        var result = AspectRatioDimensions.FromHeight(sourceWidth, sourceHeight, requestedHeight);

        Assert.Equal((expectedWidth, expectedHeight), result);
        Assert.Equal((long)sourceWidth * result.Height, (long)sourceHeight * result.Width);
    }

    [Theory]
    [InlineData(0, 1080, 1280)]
    [InlineData(1920, 0, 1280)]
    [InlineData(1920, 1080, 0)]
    [InlineData(-1, 1080, 1280)]
    public void FromWidth_ShouldRejectNonPositiveDimensions(int width, int height, int requestedWidth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AspectRatioDimensions.FromWidth(width, height, requestedWidth));
    }

    [Fact]
    public void FromHeight_ShouldRejectNonPositiveDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AspectRatioDimensions.FromHeight(1920, 1080, -1));
    }
}
