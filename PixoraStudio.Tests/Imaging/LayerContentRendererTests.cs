using PixoraStudio.Core.Imaging;
using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Tests.Imaging;

public sealed class LayerContentRendererTests
{
    [Fact]
    public void Render_EmptyLayer_IsFullyTransparent()
    {
        using var bitmap = LayerContentRenderer.Render(32, 24, PhotoLayerContentType.Empty);
        Assert.Equal(32, bitmap.Width);
        Assert.Equal(24, bitmap.Height);
        Assert.All(Enumerable.Range(0, bitmap.Height).SelectMany(y => Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x,y))), p => Assert.Equal(0, p.Alpha));
    }

    [Fact]
    public void Render_Shape_FillsInteriorWithConfiguredColour()
    {
        using var bitmap = LayerContentRenderer.Render(40, 30, PhotoLayerContentType.Shape, fillColor: "#FFFF0000");
        var pixel = bitmap.GetPixel(20,15);
        Assert.True(pixel.Red > 240); Assert.True(pixel.Green < 10); Assert.True(pixel.Blue < 10);
        Assert.Equal(255, pixel.Alpha);
    }

    [Fact]
    public void Render_Frame_LeavesCentreTransparentAndDrawsBorder()
    {
        using var bitmap = LayerContentRenderer.Render(40, 30, PhotoLayerContentType.Frame, strokeColor: "#FF00FF00", strokeWidth: 4);
        Assert.Equal(0, bitmap.GetPixel(20,15).Alpha);
        Assert.True(bitmap.GetPixel(1,15).Green > 200);
    }

    [Fact]
    public void Render_Text_ProducesVisiblePixels()
    {
        using var bitmap = LayerContentRenderer.Render(240, 100, PhotoLayerContentType.Text, text: "Pixora", fontSize: 48);
        Assert.Contains(Enumerable.Range(0, bitmap.Height).SelectMany(y => Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x,y))), p => p.Alpha > 0);
    }

    [Fact]
    public void Render_RejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LayerContentRenderer.Render(0, 10, PhotoLayerContentType.Shape));
    }
}
