using PixoraStudio.Core.Models;
using PixoraStudio.Infrastructure.Services;
using SkiaSharp;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class PixelArtUpscalingServiceTests
{
    private readonly PixelArtUpscalingService _service = new();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public void NearestNeighbour_ShouldPreserveEverySourcePixel(int scale)
    {
        using var source = CreateTestBitmap();
        using var result = _service.Upscale(source, scale, PixelArtUpscalingAlgorithm.NearestNeighbour);

        Assert.Equal(source.Width * scale, result.Width);
        Assert.Equal(source.Height * scale, result.Height);
        for (int y = 0; y < result.Height; y++)
        for (int x = 0; x < result.Width; x++)
            Assert.Equal(source.GetPixel(x / scale, y / scale), result.GetPixel(x, y));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void Scale2x_ShouldProduceExpectedDimensionsAndPreserveAlpha(int scale)
    {
        using var source = CreateTestBitmap();
        using var result = _service.Upscale(source, scale, PixelArtUpscalingAlgorithm.Scale2x);

        Assert.Equal(source.Width * scale, result.Width);
        Assert.Equal(source.Height * scale, result.Height);
        Assert.Contains((byte)0, GetAlphaValues(result));
        Assert.Contains((byte)128, GetAlphaValues(result));
        Assert.Contains((byte)255, GetAlphaValues(result));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void Scale3x_ShouldProduceExpectedDimensionsAndPreserveAlpha(int scale)
    {
        using var source = CreateTestBitmap();
        using var result = _service.Upscale(source, scale, PixelArtUpscalingAlgorithm.Scale3x);

        Assert.Equal(source.Width * scale, result.Width);
        Assert.Equal(source.Height * scale, result.Height);
        Assert.Contains((byte)0, GetAlphaValues(result));
        Assert.Contains((byte)128, GetAlphaValues(result));
        Assert.Contains((byte)255, GetAlphaValues(result));
    }

    [Theory]
    [InlineData(PixelArtUpscalingAlgorithm.Scale2x, 1)]
    [InlineData(PixelArtUpscalingAlgorithm.Scale2x, 3)]
    [InlineData(PixelArtUpscalingAlgorithm.Scale2x, 9)]
    [InlineData(PixelArtUpscalingAlgorithm.Scale3x, 1)]
    [InlineData(PixelArtUpscalingAlgorithm.Scale3x, 2)]
    [InlineData(PixelArtUpscalingAlgorithm.Scale3x, 4)]
    [InlineData(PixelArtUpscalingAlgorithm.Scale3x, 8)]
    [InlineData(PixelArtUpscalingAlgorithm.Scale3x, 16)]
    public void EdgeAwareAlgorithms_ShouldRejectUnsupportedFactors(PixelArtUpscalingAlgorithm algorithm, int scale)
    {
        using var source = CreateTestBitmap();
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Upscale(source, scale, algorithm));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Upscale_ShouldRejectScaleOutsideOneToSixteen(int scale)
    {
        using var source = CreateTestBitmap();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Upscale(source, scale, PixelArtUpscalingAlgorithm.NearestNeighbour));
    }

    [Fact]
    public void Upscale_ShouldRejectOutputDimensionsAboveLimit()
    {
        using var source = new SKBitmap(1025, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Upscale(source, 16, PixelArtUpscalingAlgorithm.NearestNeighbour));
    }

    [Fact]
    public void Upscale_ShouldRejectNullSource()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _service.Upscale(null!, 2, PixelArtUpscalingAlgorithm.NearestNeighbour));
    }

    [Fact]
    public void NearestNeighbour_OneTimes_ShouldReturnIndependentCopy()
    {
        using var source = CreateTestBitmap();
        using var result = _service.Upscale(source, 1, PixelArtUpscalingAlgorithm.NearestNeighbour);
        result.SetPixel(0, 0, SKColors.Magenta);
        Assert.NotEqual(source.GetPixel(0, 0), result.GetPixel(0, 0));
    }

    private static SKBitmap CreateTestBitmap()
    {
        var bitmap = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        bitmap.SetPixel(1, 0, new SKColor(0, 255, 0, 128));
        bitmap.SetPixel(2, 0, new SKColor(0, 0, 255, 0));
        bitmap.SetPixel(0, 1, new SKColor(255, 255, 0, 255));
        bitmap.SetPixel(1, 1, new SKColor(255, 0, 255, 128));
        bitmap.SetPixel(2, 1, new SKColor(0, 255, 255, 0));
        return bitmap;
    }

    private static HashSet<byte> GetAlphaValues(SKBitmap bitmap)
    {
        var values = new HashSet<byte>();
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            values.Add(bitmap.GetPixel(x, y).Alpha);
        return values;
    }
}
