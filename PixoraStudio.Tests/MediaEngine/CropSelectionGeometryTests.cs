using PixoraStudio.Core.Geometry;
using SkiaSharp;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class CropSelectionGeometryTests
{
    private static readonly SKRect ImageBounds = new(0, 0, 1200, 800);

    [Theory]
    [InlineData(1f)]
    [InlineData(4f / 3f)]
    [InlineData(16f / 9f)]
    [InlineData(9f / 16f)]
    public void ConstrainToAspectRatio_ShouldProduceRequestedRatio(float ratio)
    {
        var result = CropSelectionGeometry.ConstrainToAspectRatio(
            new SKRect(100, 100, 900, 650), ImageBounds, ratio);

        Assert.InRange(Math.Abs(result.Width / result.Height - ratio), 0, 0.0001f);
        Assert.True(ImageBounds.Contains(result));
    }

    [Fact]
    public void ConstrainToAspectRatio_ShouldKeepSelectionCentreWhenPossible()
    {
        var selection = new SKRect(200, 150, 800, 550);
        var result = CropSelectionGeometry.ConstrainToAspectRatio(selection, ImageBounds, 1f);

        Assert.Equal(500f, result.MidX, 3);
        Assert.Equal(350f, result.MidY, 3);
    }

    [Fact]
    public void ConstrainToAspectRatio_ShouldFitInsideImageAtEdges()
    {
        var result = CropSelectionGeometry.ConstrainToAspectRatio(
            new SKRect(900, 600, 1200, 800), ImageBounds, 16f / 9f);

        Assert.True(ImageBounds.Contains(result));
        Assert.InRange(Math.Abs(result.Width / result.Height - 16f / 9f), 0, 0.0001f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ConstrainToAspectRatio_ShouldRejectInvalidRatios(float ratio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CropSelectionGeometry.ConstrainToAspectRatio(new SKRect(1, 1, 10, 10), ImageBounds, ratio));
    }

    [Fact]
    public void ConstrainToAspectRatio_ShouldRejectSelectionOutsideImage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CropSelectionGeometry.ConstrainToAspectRatio(
                new SKRect(1300, 900, 1400, 1000), ImageBounds, 1f));
    }
}
