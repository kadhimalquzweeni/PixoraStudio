using PixoraStudio.Core.Geometry;
using SkiaSharp;

namespace PixoraStudio.Tests.Geometry;

public sealed class LayerSelectionGeometryTests
{
    [Fact]
    public void GetHandlePoints_WhenLayerIsInsideCanvas_ReturnsEightActualHandles()
    {
        var points = LayerSelectionGeometry.GetHandlePoints(new SKRect(10, 20, 70, 60));

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

    [Fact]
    public void GetHandlePoints_WhenLayerExtendsBeyondCanvas_KeepsHandlesOnActualBounds()
    {
        var points = LayerSelectionGeometry.GetHandlePoints(new SKRect(-30, -20, 130, 100));

        Assert.Equal(new SKPoint(-30, -20), points[0]);
        Assert.Equal(new SKPoint(50, -20), points[1]);
        Assert.Equal(new SKPoint(130, -20), points[2]);
        Assert.Equal(new SKPoint(130, 40), points[3]);
        Assert.Equal(new SKPoint(130, 100), points[4]);
        Assert.Equal(new SKPoint(50, 100), points[5]);
        Assert.Equal(new SKPoint(-30, 100), points[6]);
        Assert.Equal(new SKPoint(-30, 40), points[7]);
    }

    [Fact]
    public void GetHandlePoints_WhenLayerIsEntirelyOutsideCanvas_DoesNotCreateProxyHandles()
    {
        var points = LayerSelectionGeometry.GetHandlePoints(new SKRect(-200, 10, -100, 60));

        Assert.Equal(new SKPoint(-200, 10), points[0]);
        Assert.Equal(new SKPoint(-150, 10), points[1]);
        Assert.Equal(new SKPoint(-100, 10), points[2]);
        Assert.Equal(new SKPoint(-100, 35), points[3]);
        Assert.Equal(new SKPoint(-100, 60), points[4]);
        Assert.Equal(new SKPoint(-150, 60), points[5]);
        Assert.Equal(new SKPoint(-200, 60), points[6]);
        Assert.Equal(new SKPoint(-200, 35), points[7]);
    }

    [Fact]
    public void FindHandle_CanHitHandleOutsideDocumentCanvas()
    {
        var layer = new SKRect(-40, -30, 60, 60);

        var result = LayerSelectionGeometry.FindHandle(new SKPoint(-40, -30), layer, 8);

        Assert.Equal(0, result);
    }

    [Fact]
    public void FindHandle_ReturnsClosestHandleWhenHitRegionsOverlap()
    {
        var layer = new SKRect(-40, 10, 60, 60);

        var result = LayerSelectionGeometry.FindHandle(new SKPoint(-40, 35), layer, 8);

        Assert.Equal(7, result);
    }

    [Fact]
    public void FindHandle_ReturnsNullWhenPointerIsOutsideAllHandleRegions()
    {
        var result = LayerSelectionGeometry.FindHandle(
            new SKPoint(50, 40), new SKRect(10, 10, 90, 70), 5);

        Assert.Null(result);
    }

    [Fact]
    public void GetHandlePoints_RejectsInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LayerSelectionGeometry.GetHandlePoints(new SKRect(10, 10, 10, 50)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LayerSelectionGeometry.GetHandlePoints(new SKRect(0, 0, 20, float.PositiveInfinity)));
    }

    [Fact]
    public void FindHandle_RejectsNegativeHitRadius()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LayerSelectionGeometry.FindHandle(
                new SKPoint(0, 0), new SKRect(10, 10, 50, 50), -1));
    }
}
