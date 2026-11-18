using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacCodedBlockFlagContextTests
{
    [Theory]
    [InlineData(true, false, 0, 0, 3)]
    [InlineData(false, false, 0, 0, 0)]
    [InlineData(true, true, 0, 0, 1)]
    [InlineData(false, true, 0, 0, 0)]
    [InlineData(true, false, 1, 0, 1)]
    [InlineData(true, false, 0, 1, 2)]
    [InlineData(true, true, 1, 1, 3)]
    [InlineData(false, true, 1, 1, 3)]
    public void GetContextIndexIncrement_ShouldCombineNeighbourConditions(
        bool currentIntra,
        bool leftAvailable,
        int leftCoded,
        int topCoded,
        int expected)
    {
        H264CabacCodedBlockNeighbour left = new(
            leftAvailable, leftAvailable, false, leftCoded == 1);
        H264CabacCodedBlockNeighbour top = new(
            true, true, false, topCoded == 1);

        Assert.Equal(expected,
            H264CabacCodedBlockFlagContext.GetContextIndexIncrement(
                currentIntra, left, top));
    }

    [Fact]
    public void GetContextIndexIncrement_ShouldTreatPcmNeighboursAsCoded()
    {
        H264CabacCodedBlockNeighbour pcm = new(
            MacroblockAvailable: true,
            TransformBlockAvailable: false,
            IsPcmMacroblock: true,
            CodedBlockFlag: false);

        Assert.Equal(3,
            H264CabacCodedBlockFlagContext.GetContextIndexIncrement(
                false, pcm, pcm));
    }

    [Fact]
    public void GetContextIndexIncrement_ShouldTreatMissingTransformBlockAsZero()
    {
        H264CabacCodedBlockNeighbour unavailableBlock = new(
            MacroblockAvailable: true,
            TransformBlockAvailable: false,
            IsPcmMacroblock: false,
            CodedBlockFlag: true);

        Assert.Equal(0,
            H264CabacCodedBlockFlagContext.GetContextIndexIncrement(
                true, unavailableBlock, unavailableBlock));
    }
}
