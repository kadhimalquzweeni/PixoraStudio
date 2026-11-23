using PixoraStudio.Core.Imaging;
using SkiaSharp;

namespace PixoraStudio.Tests.Imaging;

public sealed class ImageLayerCompositorCanvasBackgroundTests
{
    [Fact]
    public void Composite_WithNoImageLayers_ReturnsCanvasBackground()
    {
        using var background = ImageDocumentCanvas.Create(12, 8, SKColors.White);

        using var composite = ImageLayerCompositor.Composite(
            background,
            Array.Empty<(SKBitmap Bitmap, int X, int Y, int Width, int Height, bool IsVisible)>());

        Assert.Equal(12, composite.Width);
        Assert.Equal(8, composite.Height);
        Assert.Equal(SKColors.White, composite.GetPixel(6, 4));
    }

    [Fact]
    public void Composite_WithTransparentCanvasAndNoLayers_RemainsTransparent()
    {
        using var background = ImageDocumentCanvas.Create(12, 8);

        using var composite = ImageLayerCompositor.Composite(
            background,
            Array.Empty<(SKBitmap Bitmap, int X, int Y, int Width, int Height, bool IsVisible)>());

        Assert.Equal(SKColors.Transparent, composite.GetPixel(6, 4));
    }

    [Fact]
    public void Composite_ImageLayerIsDrawnAboveCanvasBackground()
    {
        using var background = ImageDocumentCanvas.Create(4, 4, SKColors.White);
        using var layer = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        layer.Erase(SKColors.Red);
        var layers = new[]
        {
            (layer, 1, 1, 2, 2, true)
        };

        using var composite = ImageLayerCompositor.Composite(background, layers);

        Assert.Equal(SKColors.White, composite.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, composite.GetPixel(2, 2));
    }
}
