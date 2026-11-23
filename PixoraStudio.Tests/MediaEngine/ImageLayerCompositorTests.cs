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
            [(overlay, 1, 1, true)]);

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
            [(overlay, 0, 0, true)]);

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
            [(overlay, 0, 0, false)]);

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
            [(overlay, 1, 1, true)]);

        Assert.Equal(SKColors.Red, result.GetPixel(1, 1));
        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
    }
}