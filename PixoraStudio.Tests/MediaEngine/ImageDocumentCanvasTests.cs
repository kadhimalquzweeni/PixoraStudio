using PixoraStudio.Core.Imaging;
using SkiaSharp;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class ImageDocumentCanvasTests
{
    [Fact]
    public void Create_ShouldReturnTransparentCanvasWithRequestedDimensions()
    {
        using var canvas = ImageDocumentCanvas.Create(640, 480);

        Assert.Equal(640, canvas.Width);
        Assert.Equal(480, canvas.Height);
        Assert.Equal(SKColors.Transparent, canvas.GetPixel(0, 0));
        Assert.Equal(SKColors.Transparent, canvas.GetPixel(639, 479));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void Create_ShouldRejectInvalidDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImageDocumentCanvas.Create(width, height));
    }
}
