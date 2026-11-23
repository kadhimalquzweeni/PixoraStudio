using PixoraStudio.Core.Geometry;
using SkiaSharp;

namespace PixoraStudio.Tests.Geometry;

public sealed class LayerSelectionGeometryTests
{
    private static readonly SKRect Canvas = new(0, 0, 100, 80);

    [Fact]
    public void GetHandlePoints_WhenLayerIsInsideCanvas_ReturnsAllEightActualHandles()
    {
        var points = LayerSelectionGeometry.GetHandlePoints(new SKRect(10, 20, 70, 60), Canvas);

        Assert.Equal(8, points.Length);
        Assert.Equal(new SKPoint(10, 20), points[0]);
        Assert.Equal(new SKPoint(40, 20), points[1]);
        Assert.Equal(new SKPoint(70, 20), points[2]);
        Assert.Equal(new SKPoint(70, 40), points[3]);
        Assert.Equal(new SKPoint(70, 60), points[4]);
        Assert.Equal(new SKPoint(40, 60), points[5]);
        Assert.Equal(new SKPoint(10, 60), points[6]);
        Assert.Equal(new SKPoint(10, 40), points[7]);
    }

    [Theory]
    [InlineData(-30, 10, 50, 50, 0, 10)]
    [InlineData(50, 10, 130, 50, 100, 10)]
    [InlineData(10, -30, 50, 50, 10, 0)]
    [InlineData(10, 40, 50, 100, 10, 80)]
    public void GetHandlePoints_WhenLayerExtendsBeyondCanvas_ClampsOffCanvasHandles(
        float left, float top, float right, float bottom, float expectedLeft, float expectedTop)
    {
        var points = LayerSelectionGeometry.GetHandlePoints(
            new SKRect(left, top, right, bottom), Canvas);

        Assert.Equal(expectedLeft, points[0].X);
        Assert.Equal(expectedTop, points[0].Y);
        Assert.All(points, point =>
        {
            Assert.InRange(point.X, Canvas.Left, Canvas.Right);
            Assert.InRange(point.Y, Canvas.Top, Canvas.Bottom);
        });
    }

    [Fact]
    public void FindHandle_CanHitHandlePinnedToVisibleCanvasEdge()
    {
        var layer = new SKRect(-40, 10, 60, 60);

        var result = LayerSelectionGeometry.FindHandle(new SKPoint(0, 10), layer, Canvas, 8);

        Assert.Equal(0, result);
    }

    [Fact]
    public void FindHandle_ReturnsClosestHandleWhenHitRegionsOverlap()
    {
        var layer = new SKRect(-40, 10, 60, 60);

        var result = LayerSelectionGeometry.FindHandle(new SKPoint(0, 35), layer, Canvas, 8);

        Assert.Equal(7, result);
    }

    [Fact]
    public void FindHandle_ReturnsNullWhenPointerIsOutsideAllHandleRegions()
    {
        var result = LayerSelectionGeometry.FindHandle(
            new SKPoint(50, 40), new SKRect(10, 10, 90, 70), Canvas, 5);

        Assert.Null(result);
    }

    [Fact]
    public void GetHandlePoints_WhenLayerIsEntirelyOutsideCanvas_PinsHandlesToNearestEdge()
    {
        var points = LayerSelectionGeometry.GetHandlePoints(
            new SKRect(-200, 10, -100, 60), Canvas);

        Assert.All(points, point => Assert.Equal(0, point.X));
        Assert.Contains(new SKPoint(0, 10), points);
        Assert.Contains(new SKPoint(0, 35), points);
        Assert.Contains(new SKPoint(0, 60), points);
    }

    [Fact]
    public void GetHandlePoints_RejectsInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LayerSelectionGeometry.GetHandlePoints(new SKRect(10, 10, 10, 50), Canvas));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LayerSelectionGeometry.GetHandlePoints(new SKRect(0, 0, 20, 20), new SKRect(0, 0, 0, 20)));
    }

    [Fact]
    public void FindHandle_RejectsNegativeHitRadius()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LayerSelectionGeometry.FindHandle(
                new SKPoint(0, 0), new SKRect(10, 10, 50, 50), Canvas, -1));
    }
}
