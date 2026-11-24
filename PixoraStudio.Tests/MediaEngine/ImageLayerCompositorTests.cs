using PixoraStudio.Core.Imaging;
using SkiaSharp;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class ImageLayerCompositorTests
{
    [Fact]
    public void Composite_ShouldDrawOverlayAboveBackground()
    {
        using var background = new SKBitmap(2, 2);
        background.Erase(SKColors.Blue);
        using var overlay = new SKBitmap(1, 1);
        overlay.SetPixel(0, 0, SKColors.Red);

        using var result = ImageLayerCompositor.Composite(background,
            [(overlay, 1, 1, 1, 1, true)]);

        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, result.GetPixel(1, 1));
    }

    [Fact]
    public void Composite_ShouldPreserveTransparentPixels()
    {
        using var background = new SKBitmap(1, 1);
        background.SetPixel(0, 0, new SKColor(20, 40, 60));
        using var overlay = new SKBitmap(1, 1);
        overlay.SetPixel(0, 0, SKColors.Transparent);

        using var result = ImageLayerCompositor.Composite(background,
            [(overlay, 0, 0, 1, 1, true)]);

        Assert.Equal(new SKColor(20, 40, 60), result.GetPixel(0, 0));
    }

    [Fact]
    public void Composite_ShouldIgnoreHiddenLayers()
    {
        using var background = new SKBitmap(1, 1);
        background.Erase(SKColors.Blue);
        using var overlay = new SKBitmap(1, 1);
        overlay.Erase(SKColors.Red);

        using var result = ImageLayerCompositor.Composite(background,
            [(overlay, 0, 0, 1, 1, false)]);

        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
    }

    [Fact]
    public void Composite_ShouldClipLayersOutsideDocumentBounds()
    {
        using var background = new SKBitmap(2, 2);
        background.Erase(SKColors.Blue);
        using var overlay = new SKBitmap(2, 2);
        overlay.Erase(SKColors.Red);

        using var result = ImageLayerCompositor.Composite(background,
            [(overlay, 1, 1, 1, 1, true)]);

        Assert.Equal(SKColors.Red, result.GetPixel(1, 1));
        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
    }

    [Fact]
    public void Composite_ShouldResizeLayerToRequestedBounds()
    {
        using var background = new SKBitmap(4, 4);
        background.Erase(SKColors.Blue);
        using var overlay = new SKBitmap(1, 1);
        overlay.Erase(SKColors.Red);

        using var result = ImageLayerCompositor.Composite(background,
            [(overlay, 1, 1, 2, 2, true)]);

        Assert.Equal(SKColors.Red, result.GetPixel(1, 1));
        Assert.Equal(SKColors.Red, result.GetPixel(2, 2));
        Assert.Equal(SKColors.Blue, result.GetPixel(3, 3));
    }

    [Fact]
    public void Composite_ShouldIgnoreNonPositiveLayerDimensions()
    {
        using var background = new SKBitmap(2, 2);
        background.Erase(SKColors.Blue);
        using var overlay = new SKBitmap(1, 1);
        overlay.Erase(SKColors.Red);

        using var result = ImageLayerCompositor.Composite(background,
            [(overlay, 0, 0, 0, 1, true)]);

        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
    }

    [Fact]
    public void Composite_ShouldKeepEmptyDocumentCanvasTransparent()
    {
        using var canvas = new SKBitmap(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        canvas.Erase(SKColors.Transparent);

        using var result = ImageLayerCompositor.Composite(canvas, []);

        Assert.Equal(3, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal((byte)0, result.GetPixel(0, 0).Alpha);
        Assert.Equal((byte)0, result.GetPixel(2, 1).Alpha);
    }

    [Fact]
    public void Composite_ShouldKeepDocumentDimensionsWhenLayerIsResized()
    {
        using var canvas = new SKBitmap(5, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        canvas.Erase(SKColors.Transparent);
        using var firstImageLayer = new SKBitmap(2, 2);
        firstImageLayer.Erase(SKColors.Red);

        using var result = ImageLayerCompositor.Composite(canvas,
            [(firstImageLayer, 1, 1, 3, 2, true)]);

        Assert.Equal(5, result.Width);
        Assert.Equal(4, result.Height);
        Assert.Equal(SKColors.Red, result.GetPixel(1, 1));
        Assert.Equal(SKColors.Red, result.GetPixel(3, 2));
        Assert.Equal((byte)0, result.GetPixel(4, 3).Alpha);
    }

    [Fact]
    public void Composite_ShouldApplyLayerOpacity()
    {
        using var background = new SKBitmap(1, 1);
        background.Erase(SKColors.Blue);
        using var overlay = new SKBitmap(1, 1);
        overlay.Erase(SKColors.Red);

        using var result = ImageLayerCompositor.CompositeWithOpacity(background,
            [(overlay, 0, 0, 1, 1, true, 0.5f)]);

        var pixel = result.GetPixel(0, 0);
        Assert.InRange(pixel.Red, (byte)126, (byte)129);
        Assert.InRange(pixel.Blue, (byte)126, (byte)129);
        Assert.Equal((byte)255, pixel.Alpha);
    }

    [Fact]
    public void Composite_ShouldSkipFullyTransparentLayer()
    {
        using var background = new SKBitmap(1, 1);
        background.Erase(SKColors.Blue);
        using var overlay = new SKBitmap(1, 1);
        overlay.Erase(SKColors.Red);

        using var result = ImageLayerCompositor.CompositeWithOpacity(background,
            [(overlay, 0, 0, 1, 1, true, 0f)]);

        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
    }
}
