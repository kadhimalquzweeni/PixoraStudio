using PixoraStudio.Core.Geometry;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class LayerSnappingTests
{
    [Fact]
    public void Snap_ShouldAlignLayerToCanvasLeftEdge()
    {
        var result = LayerSnapping.Snap(5, 30, 40, 20, 200, 150, [], 8);
        Assert.Equal(0, result.X);
        Assert.Equal(0, result.VerticalGuide);
    }

    [Fact]
    public void Snap_ShouldAlignLayerCentreToCanvasCentre()
    {
        var result = LayerSnapping.Snap(78, 62, 40, 20, 200, 150, [], 8);
        Assert.Equal(80, result.X);
        Assert.Equal(65, result.Y);
        Assert.Equal(100, result.VerticalGuide);
        Assert.Equal(75, result.HorizontalGuide);
    }

    [Fact]
    public void Snap_ShouldAlignToOtherLayerEdgesAndCentres()
    {
        var others = new[] { new LayerSnapBounds(50, 40, 60, 30) };
        var result = LayerSnapping.Snap(108, 72, 20, 20, 300, 200, others, 8);
        Assert.Equal(110, result.X);
        Assert.Equal(70, result.Y);
        Assert.Equal(110, result.VerticalGuide);
        Assert.Equal(70, result.HorizontalGuide);
    }

    [Fact]
    public void Snap_ShouldNotMoveLayerOutsideThreshold()
    {
        var result = LayerSnapping.Snap(20, 20, 30, 30, 200, 150, [], 5);
        Assert.Equal(20, result.X);
        Assert.Equal(20, result.Y);
        Assert.Null(result.VerticalGuide);
        Assert.Null(result.HorizontalGuide);
    }

    [Fact]
    public void Snap_ShouldChooseNearestAlignment()
    {
        var others = new[] { new LayerSnapBounds(44, 0, 20, 20) };
        var result = LayerSnapping.Snap(22, 0, 20, 20, 200, 150, others, 8);
        Assert.Equal(24, result.X);
        Assert.Equal(44, result.VerticalGuide);
    }

    [Fact]
    public void Snap_ShouldRejectNegativeThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LayerSnapping.Snap(0, 0, 10, 10, 100, 100, [], -1));
    }
}
