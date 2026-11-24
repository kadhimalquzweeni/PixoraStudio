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
        // Premultiplied alpha normalizes RGB to zero for fully transparent pixels.
        // Assert alpha rather than the arbitrary RGB channels of transparent colours.
        Assert.Equal((byte)0, canvas.GetPixel(0, 0).Alpha);
        Assert.Equal((byte)0, canvas.GetPixel(639, 479).Alpha);
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
