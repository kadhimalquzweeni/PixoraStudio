using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacInterMacroblockNeighbourContextTests
{
    [Fact]
    public void GetSkipFlagContextIncrement_ShouldCountAvailableNonSkippedNeighbours()
    {
        H264CabacInterMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 1, 3, isSkipped: false, isDirect: false);
        context.RecordMacroblock(1, 1, 0, 3, isSkipped: true, isDirect: true);

        Assert.Equal(1, context.GetSkipFlagContextIncrement(1, 1, 3));
    }

    [Fact]
    public void GetSkipFlagContextIncrement_ShouldIgnoreOtherSlicesAndUnavailableNeighbours()
    {
        H264CabacInterMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 1, 2, isSkipped: false, isDirect: false);

        Assert.Equal(0, context.GetSkipFlagContextIncrement(1, 1, 3));
    }

    [Fact]
    public void GetBSliceMacroblockTypeContextIncrement_ShouldCountAvailableNonDirectNeighbours()
    {
        H264CabacInterMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 1, 3, isSkipped: false, isDirect: true);
        context.RecordMacroblock(1, 1, 0, 3, isSkipped: false, isDirect: false);

        Assert.Equal(1, context.GetBSliceMacroblockTypeContextIncrement(1, 1, 3));
    }

    [Fact]
    public void Reset_ShouldClearSpatialNeighbourState()
    {
        H264CabacInterMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 3, isSkipped: false, isDirect: false);

        context.Reset();

        Assert.Equal(0, context.GetSkipFlagContextIncrement(1, 0, 3));
        Assert.Equal(0, context.GetBSliceMacroblockTypeContextIncrement(1, 0, 3));
    }
}
