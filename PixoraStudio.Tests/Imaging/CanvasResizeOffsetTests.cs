using PixoraStudio.Core.Geometry;

namespace PixoraStudio.Tests.Imaging;

public sealed class CanvasResizeOffsetTests
{
    [Fact]
    public void Calculate_WhenKeepingContentPosition_ReturnsZeroOffset()
    {
        var offset = CanvasResizeOffset.Calculate(800, 600, 1200, 900, false);

        Assert.Equal(new CanvasResizeOffset(0, 0), offset);
    }

    [Fact]
    public void Calculate_WhenExpandingCentresExistingContent()
    {
        var offset = CanvasResizeOffset.Calculate(800, 600, 1200, 1000, true);

        Assert.Equal(new CanvasResizeOffset(200, 200), offset);
    }

    [Fact]
    public void Calculate_WhenShrinkingUsesNegativeOffsetToCropFromAllSides()
    {
        var offset = CanvasResizeOffset.Calculate(1200, 900, 800, 600, true);

        Assert.Equal(new CanvasResizeOffset(-200, -150), offset);
    }

    [Fact]
    public void Calculate_WhenCentredResizeHasOddDifferenceUsesStableTopLeftRounding()
    {
        var offset = CanvasResizeOffset.Calculate(10, 10, 9, 9, true);

        Assert.Equal(new CanvasResizeOffset(-1, -1), offset);
    }

    [Theory]
    [InlineData(0, 10, 10, 10)]
    [InlineData(10, 0, 10, 10)]
    [InlineData(10, 10, 0, 10)]
    [InlineData(10, 10, 10, 0)]
    public void Calculate_RejectsInvalidDimensions(
        int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CanvasResizeOffset.Calculate(oldWidth, oldHeight, newWidth, newHeight, true));
    }
}
