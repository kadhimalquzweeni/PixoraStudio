using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class PdfOutputDimensionsTests
{
    [Fact]
    public void FromPoints_ShouldCalculateA4PixelDimensionsAt300Dpi()
    {
        // Standard A4 page dimensions in PDF points.
        PdfPixelDimensions dimensions = PdfOutputDimensions.FromPoints(595.28, 841.89, 300);

        Assert.Equal(2481, dimensions.Width);
        Assert.Equal(3508, dimensions.Height);
    }

    [Fact]
    public void FromPoints_ShouldPreserveExactPointDimensionsAt72Dpi()
    {
        PdfPixelDimensions dimensions = PdfOutputDimensions.FromPoints(200, 100, 72);

        Assert.Equal(new PdfPixelDimensions(200, 100), dimensions);
    }

    [Theory]
    [InlineData(0, 100, 300)]
    [InlineData(100, 0, 300)]
    [InlineData(100, 100, 71)]
    [InlineData(100, 100, 601)]
    public void FromPoints_ShouldRejectInvalidDimensionsOrDpi(double width, double height, int dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfOutputDimensions.FromPoints(width, height, dpi));
    }
}
