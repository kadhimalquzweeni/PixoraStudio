using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DecodedPictureSurfaceDeblockingMetadataTests
{
    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 8)]
    public void SetDeblockingMetadata_ShouldStoreCompleteDefensiveSnapshot(
        uint chromaFormat, int chromaBlockCount)
    {
        H264DecodedPictureSurface surface = Surface(chromaFormat);
        H264DeblockingBlockMetadata[] originalLuma =
            Enumerable.Repeat(InterBlock(), 16).ToArray();
        H264DeblockingMacroblockMetadata metadata = Metadata(chromaBlockCount, originalLuma);

        surface.WriteMacroblock(0, 0, 0, false, new byte[256],
            new byte[chromaFormat == 1 ? 64 : 128],
            new byte[chromaFormat == 1 ? 64 : 128]);
        surface.SetDeblockingMetadata(0, 0, metadata);

        Assert.True(surface.HasCompleteDeblockingMetadata);
        H264DeblockingMacroblockMetadata stored =
            surface.GetDeblockingMetadata(0, 0)!;
        Assert.NotSame(metadata.LumaBlocks, stored.LumaBlocks);
        Assert.Equal(16, stored.LumaBlocks.Count);
        Assert.Equal(chromaBlockCount, stored.ChromaCbBlocks.Count);
        Assert.Equal(chromaBlockCount, stored.ChromaCrBlocks.Count);
        Assert.Equal(27, stored.QpY);

        originalLuma[0] = IntraBlock();
        Assert.False(surface.GetDeblockingMetadata(0, 0)!.LumaBlocks[0].IsIntra);
    }

    [Fact]
    public void RewritingMacroblock_ShouldInvalidatePreviouslyAttachedDeblockingMetadata()
    {
        H264DecodedPictureSurface surface = Surface(1);
        WriteMacroblock(surface, 1);
        surface.SetDeblockingMetadata(0, 0, Metadata(4));

        WriteMacroblock(surface, 1);

        Assert.False(surface.HasCompleteDeblockingMetadata);
        Assert.Null(surface.GetDeblockingMetadata(0, 0));
    }

    [Fact]
    public void FailedRewrite_ShouldInvalidateOldDeblockingMetadataAndAvailability()
    {
        H264DecodedPictureSurface surface = Surface(1);
        WriteMacroblock(surface, 1);
        surface.SetDeblockingMetadata(0, 0, Metadata(4));

        Assert.Throws<InvalidOperationException>(() => surface.WriteMacroblock(
            0, 0, 1, false,
            new ThrowingReadOnlyByteList(256), new byte[64], new byte[64]));

        Assert.False(surface.IsMacroblockDecoded(0, 0));
        Assert.Null(surface.GetDeblockingMetadata(0, 0));
        Assert.False(surface.HasCompleteDeblockingMetadata);
    }

    [Fact]
    public void SetDeblockingMetadata_ShouldRequireDecodedMacroblock()
    {
        H264DecodedPictureSurface surface = Surface(1);

        Assert.Throws<InvalidOperationException>(() =>
            surface.SetDeblockingMetadata(0, 0, Metadata(4)));
        Assert.False(surface.HasCompleteDeblockingMetadata);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 4)]
    public void SetDeblockingMetadata_ShouldRejectIncorrectChromaBlockGeometry(
        uint chromaFormat, int incorrectCount)
    {
        H264DecodedPictureSurface surface = Surface(chromaFormat);
        WriteMacroblock(surface, chromaFormat);
        H264DeblockingMacroblockMetadata metadata = Metadata(incorrectCount);

        Assert.Throws<ArgumentException>(() =>
            surface.SetDeblockingMetadata(0, 0, metadata));
    }

    [Fact]
    public void SetDeblockingMetadata_ShouldRejectInvalidQpAndSliceControls()
    {
        H264DecodedPictureSurface surface = Surface(1);
        WriteMacroblock(surface, 1);
        H264DeblockingMacroblockMetadata metadata = Metadata(4);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            surface.SetDeblockingMetadata(0, 0, metadata with { QpY = 52 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            surface.SetDeblockingMetadata(0, 0, metadata with { DisableDeblockingFilterIdc = 3 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            surface.SetDeblockingMetadata(0, 0, metadata with { AlphaOffsetDiv2 = -7 }));
    }

    [Fact]
    public void GetDeblockingMetadata_ShouldValidateMacroblockCoordinates()
    {
        H264DecodedPictureSurface surface = Surface(1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            surface.GetDeblockingMetadata(-1, 0));
    }

    private sealed class ThrowingReadOnlyByteList(int count) : IReadOnlyList<byte>
    {
        public int Count => count;

        public byte this[int index] =>
            throw new InvalidOperationException("Synthetic macroblock copy failure.");

        public IEnumerator<byte> GetEnumerator() =>
            Enumerable.Repeat((byte)0, count).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static H264DecodedPictureSurface Surface(uint chromaFormat) =>
        new(new H264SequenceParameterSet(
            0, 66, 30, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false));

    private static void WriteMacroblock(H264DecodedPictureSurface surface, uint chromaFormat)
    {
        int chromaSamples = chromaFormat == 1 ? 64 : 128;
        surface.WriteMacroblock(0, 0, 0, false, new byte[256],
            new byte[chromaSamples], new byte[chromaSamples]);
    }

    private static H264DeblockingMacroblockMetadata Metadata(
        int chromaBlockCount, H264DeblockingBlockMetadata[]? luma = null) =>
        new(27, 25, 26, 0, 0, 0, false,
            luma ?? Enumerable.Repeat(InterBlock(), 16).ToArray(),
            Enumerable.Repeat(InterBlock(), chromaBlockCount).ToArray(),
            Enumerable.Repeat(InterBlock(), chromaBlockCount).ToArray());

    private static H264DeblockingBlockMetadata InterBlock() =>
        new(false, false, null, null, null, null);

    private static H264DeblockingBlockMetadata IntraBlock() =>
        new(true, false, null, null, null, null);
}
