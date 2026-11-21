using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VideoTimelineMathTests
{
    [Theory]
    [InlineData(0, 100, 10000, 0)]
    [InlineData(50, 100, 10000, 5000)]
    [InlineData(100, 100, 10000, 10000)]
    [InlineData(-10, 100, 10000, 0)]
    [InlineData(110, 100, 10000, 10000)]
    public void GetPosition_ShouldConvertAndClampCoordinates(double x, double width, double duration, double expected)
    {
        Assert.Equal(expected, VideoTimelineMath.GetPosition(x, width, duration));
    }

    [Theory]
    [InlineData(10, 100, 20, 0, 100, 0)]
    [InlineData(50, 100, 20, 0, 100, 50)]
    [InlineData(90, 100, 20, 0, 100, 100)]
    [InlineData(-10, 100, 20, 0, 100, 0)]
    [InlineData(110, 100, 20, 0, 100, 100)]
    [InlineData(50, 100, 20, 10, 20, 15)]
    public void GetValueFromTrackClick_ShouldMapClickToSliderValue(
        double clickX,
        double trackWidth,
        double thumbWidth,
        double minimum,
        double maximum,
        double expected)
    {
        Assert.Equal(
            expected,
            VideoTimelineMath.GetValueFromTrackClick(
                clickX,
                trackWidth,
                thumbWidth,
                minimum,
                maximum),
            precision: 8);
    }

    [Theory]
    [InlineData(double.NaN, 100, 20, 0, 100)]
    [InlineData(50, 0, 0, 0, 100)]
    [InlineData(50, 100, 100, 0, 100)]
    [InlineData(50, 100, -1, 0, 100)]
    [InlineData(50, 100, 20, 100, 0)]
    public void GetValueFromTrackClick_ShouldReturnMinimumForInvalidDimensions(
        double clickX,
        double trackWidth,
        double thumbWidth,
        double minimum,
        double maximum)
    {
        Assert.Equal(
            minimum,
            VideoTimelineMath.GetValueFromTrackClick(
                clickX,
                trackWidth,
                thumbWidth,
                minimum,
                maximum));
    }

    [Theory]
    [InlineData(0, 100, 10000, 0)]
    [InlineData(5000, 100, 10000, 50)]
    [InlineData(10000, 100, 10000, 100)]
    [InlineData(-500, 100, 10000, 0)]
    [InlineData(12000, 100, 10000, 100)]
    public void GetCoordinate_ShouldConvertAndClampPositions(double position, double width, double duration, double expected)
    {
        Assert.Equal(expected, VideoTimelineMath.GetCoordinate(position, width, duration));
    }

    [Theory]
    [InlineData(50, 0, 100, 50)]
    [InlineData(-10, 0, 100, 0)]
    [InlineData(110, 0, 100, 100)]
    [InlineData(25, 10, 20, 20)]
    public void ClampSliderValue_ShouldKeepValueWithinSliderRange(
        double value,
        double minimum,
        double maximum,
        double expected)
    {
        Assert.Equal(
            expected,
            VideoTimelineMath.ClampSliderValue(value, minimum, maximum));
    }

    [Theory]
    [InlineData(double.NaN, 0, 100)]
    [InlineData(50, double.NaN, 100)]
    [InlineData(50, 0, double.PositiveInfinity)]
    [InlineData(50, 100, 0)]
    public void ClampSliderValue_ShouldReturnMinimumForInvalidValues(
        double value,
        double minimum,
        double maximum)
    {
        Assert.Equal(
            minimum,
            VideoTimelineMath.ClampSliderValue(value, minimum, maximum));
    }

    [Theory]
    [InlineData(5000, 10000, 5000)]
    [InlineData(-10, 10000, 0)]
    [InlineData(11000, 10000, 10000)]
    public void ClampPosition_ShouldKeepPositionWithinDuration(double position, double duration, double expected)
    {
        Assert.Equal(expected, VideoTimelineMath.ClampPosition(position, duration));
    }

    [Theory]
    [InlineData(0, 100, 10000)]
    [InlineData(50, 0, 10000)]
    [InlineData(50, 100, 0)]
    [InlineData(double.NaN, 100, 10000)]
    [InlineData(50, double.PositiveInfinity, 10000)]
    public void GetPosition_ShouldReturnZeroForInvalidDimensions(double x, double width, double duration)
    {
        Assert.Equal(0, VideoTimelineMath.GetPosition(x, width, duration));
    }

    [Fact]
    public void CoordinateAndPositionConversions_ShouldRoundTrip()
    {
        const double width = 842;
        const double duration = 16375;
        const double position = 7125;

        double coordinate = VideoTimelineMath.GetCoordinate(position, width, duration);
        double actual = VideoTimelineMath.GetPosition(coordinate, width, duration);

        Assert.Equal(position, actual, precision: 8);
    }
}
