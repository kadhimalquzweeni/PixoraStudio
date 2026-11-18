using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InterResidualMacroblockReaderTests
{
    private readonly H264InterResidualMacroblockReader _reader = new();

    [Theory]
    [InlineData(1, 2, 4)]
    [InlineData(2, 4, 8)]
    public void Read_WithNoCodedBlocks_ShouldCreateZeroResidualLayoutForBothChromaFormats(
        uint chromaFormat, int blocksHigh, int blocksPerComponent)
    {
        H264CavlcNeighbourContext y = new(), cb = new(), cr = new();
        H264InterResidualMacroblock result = _reader.Read(
            Bits(""), Header(), Pattern(0, 0, chromaFormat), Sps(chromaFormat),
            0, 0, 1, y, cb, cr);

        Assert.Equal(16, result.LumaBlocks.Count);
        Assert.All(result.LumaBlocks, block =>
        {
            Assert.False(block.IsCoded);
            Assert.Equal(0, block.Residual.TotalCoefficients);
        });
        Assert.Equal(2, result.ChromaComponents.Count);
        foreach (H264IntraChromaResidualComponent component in result.ChromaComponents)
        {
            Assert.Null(component.Dc);
            Assert.Equal(blocksPerComponent, component.Blocks.Count);
            Assert.All(component.Blocks, block => Assert.False(block.IsCoded));
        }
        Assert.Equal(0, y.GetPredictedNonZeroCount(1, 0, 1));
        Assert.Equal(0, cb.GetPredictedNonZeroCount(1, 0, 1));
        Assert.Equal(0, cr.GetPredictedNonZeroCount(1, 0, 1));
    }

    [Fact]
    public void Read_ShouldDecodeCodedLumaGroupsInMacroblockScanOrder()
    {
        // Four zero coeff_token values in the top-left 8x8 luma group.
        H264BitReader bits = Bits("1111");
        H264InterResidualMacroblock result = _reader.Read(
            bits, Header(), Pattern(1, 0, 1), Sps(1),
            0, 0, 4, new(), new(), new());

        Assert.Equal(4, result.LumaBlocks.Count(block => block.IsCoded));
        Assert.Equal(new[] { (0, 0), (1, 0), (0, 1), (1, 1) },
            result.LumaBlocks.Where(block => block.IsCoded)
                .Select(block => (block.BlockX, block.BlockY)));
        Assert.Equal(0, bits.RemainingBits);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 8)]
    public void Read_WithChromaDcOnly_ShouldReadCbThenCrDcAndNoAc(
        uint chromaFormat, int dcCapacity)
    {
        string zeroDcToken = chromaFormat == 1 ? "01" : "1";
        H264BitReader bits = Bits(zeroDcToken + zeroDcToken);
        H264InterResidualMacroblock result = _reader.Read(
            bits, Header(), Pattern(0, 1, chromaFormat), Sps(chromaFormat),
            0, 0, 2, new(), new(), new());

        Assert.All(result.ChromaComponents, component =>
        {
            Assert.NotNull(component.Dc);
            Assert.Equal(dcCapacity, component.Dc.MaximumCoefficients);
            Assert.All(component.Blocks, block => Assert.False(block.IsCoded));
        });
        Assert.Equal(0, bits.RemainingBits);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void Read_WithChromaDcAndAc_ShouldReadBothDcBlocksBeforeCbAndCrAc(
        uint chromaFormat, int acTokenCount)
    {
        // Two DC coeff_token values followed by all Cb AC then all Cr AC.
        string zeroDcToken = chromaFormat == 1 ? "01" : "1";
        H264BitReader bits = Bits(zeroDcToken + zeroDcToken + new string('1', acTokenCount));
        H264InterResidualMacroblock result = _reader.Read(
            bits, Header(), Pattern(0, 2, chromaFormat), Sps(chromaFormat),
            0, 0, 3, new(), new(), new());

        Assert.All(result.ChromaComponents, component =>
        {
            Assert.NotNull(component.Dc);
            Assert.All(component.Blocks, block =>
            {
                Assert.True(block.IsCoded);
                Assert.Equal(0, block.Residual.TotalCoefficients);
            });
        });
        Assert.Equal(0, bits.RemainingBits);
    }

    [Fact]
    public void Read_ShouldMaintainIndependentPictureRelativeContextsAcrossMacroblocks()
    {
        H264CavlcNeighbourContext y = new(), cb = new(), cr = new();
        _reader.Read(Bits(""), Header(0), Pattern(0, 0, 1), Sps(1),
            0, 0, 8, y, cb, cr);
        _reader.Read(Bits(""), Header(1), Pattern(0, 0, 1), Sps(1),
            1, 0, 8, y, cb, cr);

        Assert.Equal(0, y.GetPredictedNonZeroCount(4, 0, 8));
        Assert.Equal(0, cb.GetPredictedNonZeroCount(2, 0, 8));
        Assert.Equal(0, cr.GetPredictedNonZeroCount(2, 0, 8));
    }

    [Fact]
    public void Read_ShouldRejectInvalidHeaderPatternAndCoordinates()
    {
        Assert.Throws<ArgumentException>(() => _reader.Read(
            Bits(""), Header(isIntra: true), Pattern(0, 0, 1), Sps(1),
            0, 0, 1, new(), new(), new()));
        Assert.Throws<FormatException>(() => _reader.Read(
            Bits(""), Header(), Pattern(16, 0, 1), Sps(1),
            0, 0, 1, new(), new(), new()));
        Assert.Throws<FormatException>(() => _reader.Read(
            Bits(""), Header(), Pattern(0, 0, 2), Sps(1),
            0, 0, 1, new(), new(), new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _reader.Read(
            Bits(""), Header(), Pattern(0, 0, 1), Sps(1),
            1, 0, 1, new(), new(), new()));
    }

    [Fact]
    public void Read_ShouldRejectSharedContextsAndUnsupportedTransformOrPictureMode()
    {
        H264CavlcNeighbourContext shared = new();
        Assert.Throws<ArgumentException>(() => _reader.Read(
            Bits(""), Header(), Pattern(0, 0, 1), Sps(1),
            0, 0, 1, shared, shared, new()));
        Assert.Throws<NotSupportedException>(() => _reader.Read(
            Bits(""), Header(), Pattern(0, 0, 1), Sps(1),
            0, 0, 1, new(), new(), new(), transformSize8x8Flag: true));
        Assert.Throws<NotSupportedException>(() => _reader.Read(
            Bits(""), Header(), Pattern(0, 0, 1), Sps(1) with { FrameMbsOnlyFlag = false },
            0, 0, 1, new(), new(), new()));
    }

    private static H264MacroblockHeader Header(uint address = 0, bool isIntra = false) =>
        new(address, 0, false, 0, isIntra, false, null, null, null, false);

    private static H264CodedBlockPattern Pattern(uint luma, uint chroma, uint format) =>
        new(0, luma, chroma, format);

    private static H264SequenceParameterSet Sps(uint chromaFormat) =>
        new(0, 100, 40, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 32, 16, false);

    private static H264BitReader Bits(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1')
                bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}
