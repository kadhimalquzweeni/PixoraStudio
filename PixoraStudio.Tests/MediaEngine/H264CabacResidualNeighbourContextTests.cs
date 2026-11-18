using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacResidualNeighbourContextTests
{
    [Fact]
    public void GetContextIndexIncrement_ShouldUsePreviouslyDecodedLeftAndTopBlocks()
    {
        H264CabacResidualNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, isPcm: false);
        context.RecordBlock(0, 0, 0, 1, codedBlockFlag: true);
        context.RecordBlock(0, 1, 0, 1, codedBlockFlag: false);

        Assert.Equal(1, context.GetContextIndexIncrement(
            0, 1, 1, 4, 4, 1, currentMacroblockIsIntra: true));
    }

    [Fact]
    public void GetContextIndexIncrement_ShouldTreatPictureEdgesAsUnavailable()
    {
        H264CabacResidualNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, isPcm: false);

        Assert.Equal(3, context.GetContextIndexIncrement(
            0, 0, 0, 4, 4, 1, currentMacroblockIsIntra: true));
        Assert.Equal(0, context.GetContextIndexIncrement(
            0, 0, 0, 4, 4, 1, currentMacroblockIsIntra: false));
    }

    [Fact]
    public void GetContextIndexIncrement_ShouldTreatDifferentSliceNeighboursAsUnavailable()
    {
        H264CabacResidualNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, isPcm: false);
        context.RecordBlock(0, 3, 0, 1, codedBlockFlag: true);

        Assert.Equal(1, context.GetContextIndexIncrement(
            0, 4, 0, 4, 4, 2, currentMacroblockIsIntra: true));
    }

    [Fact]
    public void GetContextIndexIncrement_ShouldTreatPcmMacroblockAsCoded()
    {
        H264CabacResidualNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, isPcm: true);

        Assert.Equal(1, context.GetContextIndexIncrement(
            0, 1, 0, 4, 4, 1, currentMacroblockIsIntra: false));
    }

    [Fact]
    public void GetContextIndexIncrement_ShouldTreatDecodedMacroblockWithoutBlockStateAsZero()
    {
        H264CabacResidualNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, isPcm: false);

        Assert.Equal(0, context.GetContextIndexIncrement(
            0, 1, 0, 4, 4, 1, currentMacroblockIsIntra: true));
    }

    [Fact]
    public void GetContextIndexIncrement_ShouldKeepComponentPlanesIndependent()
    {
        H264CabacResidualNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, isPcm: true);
        context.RecordMacroblock(1, 0, 0, 1, isPcm: false);
        context.RecordBlock(1, 0, 0, 1, codedBlockFlag: false);

        Assert.Equal(1, context.GetContextIndexIncrement(
            0, 1, 0, 4, 4, 1, currentMacroblockIsIntra: false));
        Assert.Equal(0, context.GetContextIndexIncrement(
            1, 1, 0, 2, 2, 1, currentMacroblockIsIntra: false));
    }

    [Fact]
    public void Reset_ShouldClearMacroblockAndBlockState()
    {
        H264CabacResidualNeighbourContext context = new();
        context.RecordMacroblock(0, 0, 0, 1, isPcm: true);

        context.Reset();

        Assert.Equal(0, context.GetContextIndexIncrement(
            0, 1, 0, 4, 4, 1, currentMacroblockIsIntra: false));
    }

    [Fact]
    public void RecordMacroblock_ShouldRejectInvalidPlane()
    {
        H264CabacResidualNeighbourContext context = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            context.RecordMacroblock(3, 0, 0, 1, isPcm: false));
    }
}
