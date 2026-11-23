using PixoraStudio.Core.Imaging;
using SkiaSharp;

namespace PixoraStudio.Tests.Imaging;

public sealed class ImageDocumentCanvasTests
{
    [Fact]
    public void Create_UsesTransparentBackgroundByDefault()
    {
        using var canvas = ImageDocumentCanvas.Create(16, 12);

        Assert.Equal(16, canvas.Width);
        Assert.Equal(12, canvas.Height);
        Assert.Equal(SKColors.Transparent, canvas.GetPixel(0, 0));
    }

    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    [InlineData(30, 120, 210)]
    public void Create_FillsCanvasWithRequestedBackground(int red, int green, int blue)
    {
        var background = new SKColor((byte)red, (byte)green, (byte)blue);

        using var canvas = ImageDocumentCanvas.Create(8, 6, background);

        Assert.Equal(background, canvas.GetPixel(4, 3));
    }

    [Fact]
    public void Resize_PreservesExistingPixelsAtRequestedOffsetAndFillsNewArea()
    {
        using var source = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        source.Erase(SKColors.Red);
        source.SetPixel(1, 1, SKColors.Lime);
        var background = SKColors.Blue;

        using var resized = ImageDocumentCanvas.Resize(
            source, 4, 4, 1, 1, background);

        Assert.Equal(background, resized.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, resized.GetPixel(1, 1));
        Assert.Equal(SKColors.Lime, resized.GetPixel(2, 2));
        Assert.Equal(background, resized.GetPixel(3, 3));
    }

    [Fact]
    public void Resize_PreservesSemiTransparentBackgroundAlpha()
    {
        var background = new SKColor(40, 80, 120, 128);
        using var source = ImageDocumentCanvas.Create(2, 2, background);
        var expected = source.GetPixel(0, 0);

        using var resized = ImageDocumentCanvas.Resize(
            source, 4, 4, 1, 1, background);

        Assert.Equal(expected, resized.GetPixel(1, 1));
        Assert.Equal(expected, resized.GetPixel(0, 0));
    }

    [Fact]
    public void Resize_NegativeOffsetCropsContentOutsideNewCanvas()
    {
        using var source = new SKBitmap(3, 3, SKColorType.Rgba8888, SKAlphaType.Premul);
        source.Erase(SKColors.Red);
        source.SetPixel(1, 1, SKColors.Lime);

        using var resized = ImageDocumentCanvas.Resize(
            source, 2, 2, -1, -1, SKColors.Transparent);

        Assert.Equal(SKColors.Lime, resized.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, resized.GetPixel(1, 1));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void Create_RejectsInvalidDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImageDocumentCanvas.Create(width, height));
    }

    [Fact]
    public void Resize_RejectsNullSource()
    {
        Assert.Throws<ArgumentNullException>(
            () => ImageDocumentCanvas.Resize(null!, 10, 10, 0, 0, SKColors.Transparent));
    }

    [Fact]
    public void Resize_RejectsInvalidDimensions()
    {
        using var source = new SKBitmap(2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImageDocumentCanvas.Resize(source, 0, 10, 0, 0, SKColors.Transparent));
    }
}
