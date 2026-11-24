using PixoraStudio.Core.Imaging;
using SkiaSharp;

namespace PixoraStudio.Tests.Imaging;

public sealed class GpuImageRendererDocumentBackgroundTests
{
    [Fact]
    public void Render_DrawsSolidDocumentBackgroundOverCheckerboard()
    {
        using var background = ImageDocumentCanvas.Create(4, 4, SKColors.White);
        using var output = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);

        GpuImageRenderer.Render(canvas, 4, 4, Array.Empty<GpuImageLayer>(),
            documentBackground: background);

        Assert.Equal(SKColors.White, output.GetPixel(2, 2));
    }

    [Fact]
    public void Render_LeavesCheckerboardVisibleForTransparentDocumentBackground()
    {
        using var background = ImageDocumentCanvas.Create(24, 24);
        using var output = new SKBitmap(24, 24, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);

        GpuImageRenderer.Render(canvas, 24, 24, Array.Empty<GpuImageLayer>(),
            documentBackground: background);

        Assert.Equal(new SKColor(216, 216, 216), output.GetPixel(4, 4));
        Assert.Equal(new SKColor(240, 240, 240), output.GetPixel(16, 4));
    }

    [Fact]
    public void Render_DrawsImageLayersAboveDocumentBackground()
    {
        using var background = ImageDocumentCanvas.Create(4, 4, SKColors.White);
        using var layer = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        layer.Erase(SKColors.Red);
        using var output = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        var layers = new[]
        {
            new GpuImageLayer(layer, 1, 1, 2, 2, true, new PixoraStudio.Core.Models.ImageAdjustment())
        };

        GpuImageRenderer.Render(canvas, 4, 4, layers, documentBackground: background);

        Assert.Equal(SKColors.White, output.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, output.GetPixel(2, 2));
    }
}
