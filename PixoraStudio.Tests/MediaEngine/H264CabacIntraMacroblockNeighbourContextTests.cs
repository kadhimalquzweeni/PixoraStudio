using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacIntraMacroblockNeighbourContextTests
{
    [Fact]
    public void GetMacroblockTypeContextIncrement_ShouldCountAvailableNonNxNNeighbours()
    {
        H264CabacIntraMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, true, false, false, 0, 0, 1, null);
        context.RecordMacroblock(1, 1, 0, 1, false, true, false, 15, 2, 1, 1);
        context.RecordMacroblock(2, 0, 1, 1, false, false, true, 0, 0, null, null);

        Assert.Equal(1, context.GetMacroblockTypeContextIncrement(1, 1, 1));
    }

    [Fact]
    public void GetMacroblockTypeContextIncrement_ShouldIgnoreUnavailableAndOtherSliceNeighbours()
    {
        H264CabacIntraMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 2, false, true, false, 15, 2, 1, 1);

        Assert.Equal(0, context.GetMacroblockTypeContextIncrement(1, 0, 1));
    }

    [Fact]
    public void GetChromaPredictionModeContextIncrement_ShouldCountOnlyNonZeroNonPcmNeighbours()
    {
        H264CabacIntraMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, true, false, false, 0, 0, 2, null);
        context.RecordMacroblock(1, 1, 0, 1, false, false, false, 0, 0, 0, null);
        context.RecordMacroblock(2, 0, 1, 1, false, false, true, 0, 0, null, null);

        Assert.Equal(1, context.GetChromaPredictionModeContextIncrement(1, 1, 1));
    }

    [Fact]
    public void GetQpDeltaContextIncrement_ShouldUsePreviousDecodedMacroblock()
    {
        H264CabacIntraMacroblockNeighbourContext context = new();
        context.RecordMacroblock(4, 0, 1, 1, false, true, false, 15, 2, 1, 3);

        Assert.Equal(1, context.GetQpDeltaContextIncrement(4, 1));
        Assert.Equal(0, context.GetQpDeltaContextIncrement(null, 1));
        Assert.Equal(0, context.GetQpDeltaContextIncrement(4, 2));
    }

    [Fact]
    public void GetQpDeltaContextIncrement_ShouldReturnZeroForZeroDeltaOrEmptyNonIntra16Macroblock()
    {
        H264CabacIntraMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, true, false, false, 15, 2, 1, 0);
        context.RecordMacroblock(1, 1, 0, 1, true, false, false, 0, 0, 1, 1);

        Assert.Equal(0, context.GetQpDeltaContextIncrement(0, 1));
        Assert.Equal(0, context.GetQpDeltaContextIncrement(1, 1));
    }

    [Fact]
    public void GetLumaCodedBlockPatternContextIncrements_ShouldDeriveLeftAndTopConditions()
    {
        H264CabacIntraMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, false, false, false, 0b0010, 0, 0, null);
        context.RecordMacroblock(1, 1, 0, 1, false, false, false, 0b0001, 0, 0, null);

        int[] increments = context.GetLumaCodedBlockPatternContextIncrements(1, 1, 1, previouslyDecodedCodedBlockPattern: 0b0010);

        Assert.Equal(new[] { 2, 3, 3, 1 }, increments);
    }

    [Fact]
    public void GetChromaCodedBlockPatternContextIncrements_ShouldUseSeparateFirstAndSecondBinRules()
    {
        H264CabacIntraMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, false, false, false, 0, 1, 0, null);
        context.RecordMacroblock(1, 1, 0, 1, false, false, false, 0, 2, 0, null);

        Assert.Equal(new[] { 3, 2 },
            context.GetChromaCodedBlockPatternContextIncrements(1, 1, 1));
    }

    [Fact]
    public void Reset_ShouldClearAllNeighbourState()
    {
        H264CabacIntraMacroblockNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, false, true, false, 15, 2, 1, 1);

        context.Reset();

        Assert.Equal(0, context.GetMacroblockTypeContextIncrement(1, 0, 1));
        Assert.Equal(0, context.GetChromaPredictionModeContextIncrement(1, 0, 1));
    }

    [Fact]
    public void RecordMacroblock_ShouldRejectInvalidCodedBlockPattern()
    {
        H264CabacIntraMacroblockNeighbourContext context = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            context.RecordMacroblock(0, 0, 0, 1, false, false, false, 16, 0, 0, null));
    }
}
