using PixoraStudio.Core.Imaging;
using PixoraStudio.Core.Models;
using SkiaSharp;
using PixoraStudio.Infrastructure.Services;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class GpuImageRendererTests
{
    private static SKBitmap CreateBitmap(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        return bitmap;
    }

    private static SKBitmap Render(
        int width,
        int height,
        params GpuImageLayer[] layers)
    {
        var output = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        GpuImageRenderer.Render(canvas, width, height, layers, checkerboardBackground: false);
        return output;
    }

    [Fact]
    public void Render_ShouldCompositeLayersInBottomToTopOrder()
    {
        using var bottom = CreateBitmap(1, 1, SKColors.Red);
        using var top = CreateBitmap(1, 1, SKColors.Blue);

        using var result = Render(1, 1,
            new GpuImageLayer(bottom, 0, 0, 1, 1, true, new ImageAdjustment()),
            new GpuImageLayer(top, 0, 0, 1, 1, true, new ImageAdjustment()));

        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
    }

    [Fact]
    public void Render_ShouldIgnoreHiddenLayers()
    {
        using var bitmap = CreateBitmap(1, 1, SKColors.Red);

        using var result = Render(1, 1,
            new GpuImageLayer(bitmap, 0, 0, 1, 1, false, new ImageAdjustment()));

        Assert.Equal((byte)0, result.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void Render_ShouldClipLayersToDocumentBounds()
    {
        using var bitmap = CreateBitmap(2, 2, SKColors.Red);

        using var result = Render(1, 1,
            new GpuImageLayer(bitmap, 0, 0, 2, 2, true, new ImageAdjustment()));

        Assert.Equal(SKColors.Red, result.GetPixel(0, 0));
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
    }

    [Fact]
    public void Render_ShouldApplyBrightnessAndPreserveAlpha()
    {
        using var bitmap = CreateBitmap(1, 1, new SKColor(80, 100, 120, 128));

        using var result = Render(1, 1,
            new GpuImageLayer(bitmap, 0, 0, 1, 1, true,
                new ImageAdjustment { Brightness = 20f }));

        var pixel = result.GetPixel(0, 0);
        Assert.Equal((byte)128, pixel.Alpha);
        Assert.InRange(pixel.Red, (byte)98, (byte)102);
        Assert.InRange(pixel.Green, (byte)118, (byte)122);
        Assert.InRange(pixel.Blue, (byte)138, (byte)142);
    }

    [Fact]
    public void Render_NeutralAdjustment_ShouldPreserveSemiTransparentPixel()
    {
        using var bitmap = CreateBitmap(1, 1, new SKColor(80, 120, 160, 90));
        var expected = bitmap.GetPixel(0, 0);

        using var result = Render(1, 1,
            new GpuImageLayer(bitmap, 0, 0, 1, 1, true, new ImageAdjustment()));

        var actual = result.GetPixel(0, 0);
        Assert.Equal(expected.Alpha, actual.Alpha);
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 2);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 2);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 2);
    }

    [Fact]
    public void Render_GrayscaleFilter_ShouldProduceEqualRgbChannels()
    {
        using var bitmap = CreateBitmap(1, 1, new SKColor(200, 80, 30));

        using var result = Render(1, 1,
            new GpuImageLayer(bitmap, 0, 0, 1, 1, true,
                new ImageAdjustment { Filter = ImageFilter.Grayscale }));

        var pixel = result.GetPixel(0, 0);
        Assert.Equal(pixel.Red, pixel.Green);
        Assert.Equal(pixel.Green, pixel.Blue);
    }

    [Fact]
    public void Render_SepiaFilter_ShouldProduceWarmColourBalance()
    {
        using var bitmap = CreateBitmap(1, 1, new SKColor(100, 100, 100));

        using var result = Render(1, 1,
            new GpuImageLayer(bitmap, 0, 0, 1, 1, true,
                new ImageAdjustment { Filter = ImageFilter.Sepia }));

        var pixel = result.GetPixel(0, 0);
        Assert.True(pixel.Red > pixel.Blue);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void Render_ShouldRejectInvalidDocumentDimensions(int width, int height)
    {
        using var canvas = new SKBitmap(1, 1);
        using var skCanvas = new SKCanvas(canvas);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GpuImageRenderer.Render(skCanvas, width, height, []));
    }

    [Fact]
    public void Render_Adjustments_ShouldMatchCpuProcessingForRepresentativeCases()
    {
        using var source = new SKBitmap(3, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        source.SetPixel(0, 0, new SKColor(35, 60, 95));
        source.SetPixel(1, 0, new SKColor(160, 120, 80, 180));
        source.SetPixel(2, 0, new SKColor(210, 225, 240));

        ImageAdjustment[] adjustments =
        [
            new() { Brightness = 17f, Contrast = 1.2f, Saturation = 0.65f, Exposure = 0.4f },
            new() { Highlights = -65f, Shadows = 55f },
            new() { Temperature = 75f, Tint = -30f },
            new() { Brightness = -12f, Contrast = 0.8f, Filter = ImageFilter.Grayscale },
            new() { Saturation = 1.4f, Temperature = 20f, Tint = 35f, Filter = ImageFilter.Sepia }
        ];

        var cpuService = new ImageProcessingService();
        foreach (var adjustment in adjustments)
        {
            using var expected = cpuService.ApplyAdjustments(source, adjustment);
            using var actual = Render(source.Width, source.Height,
                new GpuImageLayer(source, 0, 0, source.Width, source.Height, true, adjustment));

            for (int x = 0; x < source.Width; x++)
            {
                var expectedPixel = expected.GetPixel(x, 0);
                var actualPixel = actual.GetPixel(x, 0);

                Assert.Equal(expectedPixel.Alpha, actualPixel.Alpha);
                Assert.InRange(Math.Abs(expectedPixel.Red - actualPixel.Red), 0, 2);
                Assert.InRange(Math.Abs(expectedPixel.Green - actualPixel.Green), 0, 2);
                Assert.InRange(Math.Abs(expectedPixel.Blue - actualPixel.Blue), 0, 2);
            }
        }
    }

    [Fact]
    public void Render_ShouldDrawCheckerboardBehindTransparentLayers()
    {
        using var output = new SKBitmap(24, 24, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);

        GpuImageRenderer.Render(canvas, 24, 24, []);

        Assert.Equal(new SKColor(216, 216, 216), output.GetPixel(0, 0));
        Assert.Equal(new SKColor(240, 240, 240), output.GetPixel(12, 0));
        Assert.Equal(new SKColor(240, 240, 240), output.GetPixel(0, 12));
        Assert.Equal(new SKColor(216, 216, 216), output.GetPixel(12, 12));
    }

    [Fact]
    public void Render_ShouldRejectNullArguments()
    {
        using var bitmap = CreateBitmap(1, 1, SKColors.Red);
        using var canvas = new SKCanvas(bitmap);

        Assert.Throws<ArgumentNullException>(() =>
            GpuImageRenderer.Render(null!, 1, 1, []));
        Assert.Throws<ArgumentNullException>(() =>
            GpuImageRenderer.Render(canvas, 1, 1, null!));
    }

    [Fact]
    public void Render_ShouldApplyPerLayerOpacity()
    {
        using var background = CreateBitmap(1, 1, SKColors.Blue);
        using var overlay = CreateBitmap(1, 1, SKColors.Red);

        using var result = Render(1, 1,
            new GpuImageLayer(background, 0, 0, 1, 1, true, new ImageAdjustment()),
            new GpuImageLayer(overlay, 0, 0, 1, 1, true, new ImageAdjustment(), 0.5f));

        var pixel = result.GetPixel(0, 0);
        Assert.Equal((byte)255, pixel.Alpha);
        Assert.InRange(pixel.Red, (byte)126, (byte)129);
        Assert.InRange(pixel.Blue, (byte)126, (byte)129);
    }
}
