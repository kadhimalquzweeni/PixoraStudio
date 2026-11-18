using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraLumaResidualMacroblockReaderTests
{
    private readonly H264IntraLumaResidualMacroblockReader _reader = new();

    [Fact]
    public void Read_IntraNxN_ShouldReturnSixteenUncodedBlocksAndRecordThem()
    {
        H264CavlcNeighbourContext neighbours = new();
        H264IntraLumaResidualMacroblock result = _reader.Read(
            CreateReader(""), CreateIntraNxNHeader(), CreateSyntax(0),
            CreateMonochromeSps(), 0, 0, 3, neighbours);

        Assert.Null(result.Intra16x16Dc);
        Assert.Equal(16, result.Blocks.Count);
        Assert.All(result.Blocks, block =>
        {
            Assert.False(block.IsCoded);
            Assert.Equal(16, block.Residual.MaximumCoefficients);
            Assert.Equal(0, block.Residual.TotalCoefficients);
        });
        Assert.Equal(0, neighbours.GetPredictedNonZeroCount(1, 0, 3));
        Assert.Equal(0, neighbours.GetPredictedNonZeroCount(0, 1, 3));
    }

    [Fact]
    public void Read_IntraNxN_ShouldDecodeOnlyBlocksInCodedEightByEightGroups()
    {
        // Four zero coeff_token values for the upper-left 8x8 region.
        H264CavlcNeighbourContext neighbours = new();
        H264BitReader bits = CreateReader("1111");

        H264IntraLumaResidualMacroblock result = _reader.Read(
            bits, CreateIntraNxNHeader(), CreateSyntax(1),
            CreateMonochromeSps(), 0, 0, 1, neighbours);

        Assert.Equal(4, result.Blocks.Count(block => block.IsCoded));
        Assert.Equal(12, result.Blocks.Count(block => !block.IsCoded));
        Assert.All(result.Blocks.Where(block => block.IsCoded),
            block => Assert.Equal(0, block.Residual.TotalCoefficients));
        Assert.Equal(0, bits.RemainingBits);
    }

    [Fact]
    public void Read_IntraNxN_ShouldUseH264MacroblockBlockScanOrder()
    {
        H264IntraLumaResidualMacroblock result = _reader.Read(
            CreateReader(""), CreateIntraNxNHeader(), CreateSyntax(0),
            CreateMonochromeSps(), 0, 0, 1, new H264CavlcNeighbourContext());

        Assert.Equal(
            new[]
            {
                (0, 0), (1, 0), (0, 1), (1, 1),
                (2, 0), (3, 0), (2, 1), (3, 1),
                (0, 2), (1, 2), (0, 3), (1, 3),
                (2, 2), (3, 2), (2, 3), (3, 3)
            },
            result.Blocks.Select(block => (block.BlockX, block.BlockY)));
    }

    [Fact]
    public void Read_Intra16x16_ShouldReadDcBeforeAcAndUseFifteenCoefficientAcBlocks()
    {
        // One zero coeff_token for luma DC and one for each of 16 AC blocks.
        H264BitReader bits = CreateReader(new string('1', 17));
        H264CavlcNeighbourContext neighbours = new();

        H264IntraLumaResidualMacroblock result = _reader.Read(
            bits, CreateIntra16x16Header(), CreateSyntax(15),
            CreateMonochromeSps(), 0, 0, 2, neighbours);

        Assert.NotNull(result.Intra16x16Dc);
        Assert.Equal(16, result.Intra16x16Dc.MaximumCoefficients);
        Assert.Equal(16, result.Blocks.Count);
        Assert.All(result.Blocks, block =>
        {
            Assert.True(block.IsCoded);
            Assert.Equal(15, block.Residual.MaximumCoefficients);
        });
        Assert.Equal(0, bits.RemainingBits);
    }

    [Fact]
    public void Read_Intra16x16_ShouldReadDcEvenWhenNoAcGroupsAreCoded()
    {
        H264BitReader bits = CreateReader("1");

        H264IntraLumaResidualMacroblock result = _reader.Read(
            bits, CreateIntra16x16Header(), CreateSyntax(0),
            CreateMonochromeSps(), 0, 0, 1, new H264CavlcNeighbourContext());

        Assert.NotNull(result.Intra16x16Dc);
        Assert.All(result.Blocks, block => Assert.False(block.IsCoded));
        Assert.Equal(0, bits.RemainingBits);
    }

    [Fact]
    public void Read_ShouldUsePictureRelativeCoordinatesAcrossMacroblocks()
    {
        H264CavlcNeighbourContext neighbours = new();
        _reader.Read(CreateReader(""), CreateIntraNxNHeader(0),
            CreateSyntax(0), CreateMonochromeSps(), 0, 0, 7, neighbours);

        Assert.Equal(0, neighbours.GetPredictedNonZeroCount(4, 0, 7));
        Assert.Equal(0, neighbours.GetPredictedNonZeroCount(0, 4, 7));
    }

    [Fact]
    public void Read_ShouldRejectChromaResidualsUntilChromaLayoutIsSupported()
    {
        H264IntraMacroblockSyntax syntax = CreateSyntax(0) with
        {
            CodedBlockPattern = new H264CodedBlockPattern(0, 0, 1, 1)
        };

        Assert.Throws<NotSupportedException>(() => _reader.Read(
            CreateReader(""), CreateIntraNxNHeader(), syntax,
            CreateMonochromeSps() with { ChromaFormatIdc = 1 },
            0, 0, 1, new H264CavlcNeighbourContext()));
    }

    [Fact]
    public void Read_ShouldRejectIntra8x8TransformResiduals()
    {
        H264IntraMacroblockSyntax syntax = CreateSyntax(0) with
        {
            TransformSize8X8Flag = true
        };

        Assert.Throws<NotSupportedException>(() => _reader.Read(
            CreateReader(""), CreateIntraNxNHeader(), syntax,
            CreateMonochromeSps(), 0, 0, 1, new H264CavlcNeighbourContext()));
    }

    [Fact]
    public void Read_ShouldRejectInterMacroblocks()
    {
        H264MacroblockHeader inter = new(0, 0, false, 0,
            false, false, null, null, null, false);

        Assert.Throws<ArgumentException>(() => _reader.Read(
            CreateReader(""), inter, CreateSyntax(0),
            CreateMonochromeSps(), 0, 0, 1, new H264CavlcNeighbourContext()));
    }

    [Fact]
    public void Read_ShouldRejectNegativeCoordinatesAndSliceId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _reader.Read(
            CreateReader(""), CreateIntraNxNHeader(), CreateSyntax(0),
            CreateMonochromeSps(), -1, 0, 1, new H264CavlcNeighbourContext()));

        Assert.Throws<ArgumentOutOfRangeException>(() => _reader.Read(
            CreateReader(""), CreateIntraNxNHeader(), CreateSyntax(0),
            CreateMonochromeSps(), 0, 0, -1, new H264CavlcNeighbourContext()));
    }


    [Fact]
    public void Read_ShouldRejectCodedBlockPatternOutsideLumaRange()
    {
        H264IntraMacroblockSyntax syntax = CreateSyntax(16);

        Assert.Throws<FormatException>(() => _reader.Read(
            CreateReader(""), CreateIntraNxNHeader(), syntax,
            CreateMonochromeSps(), 0, 0, 1, new H264CavlcNeighbourContext()));
    }

    [Fact]
    public void Read_ShouldRejectMacroblockOutsidePictureBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _reader.Read(
            CreateReader(""), CreateIntraNxNHeader(), CreateSyntax(0),
            CreateMonochromeSps(), 1, 0, 1, new H264CavlcNeighbourContext()));
    }

    [Fact]
    public void Read_ShouldRejectFieldCodedPicturesUntilFieldBlockAddressingIsSupported()
    {
        Assert.Throws<NotSupportedException>(() => _reader.Read(
            CreateReader(""), CreateIntraNxNHeader(), CreateSyntax(0),
            CreateMonochromeSps() with { FrameMbsOnlyFlag = false },
            0, 0, 1, new H264CavlcNeighbourContext()));
    }

    private static H264MacroblockHeader CreateIntraNxNHeader(uint address = 0) =>
        new(address, 0, false, 0, true, false, null, null, null, false);

    private static H264MacroblockHeader CreateIntra16x16Header() =>
        new(0, 1, false, 0, true, true, 0, 0, 0, false);

    private static H264IntraMacroblockSyntax CreateSyntax(uint lumaPattern) =>
        new(null, null, null,
            new H264CodedBlockPattern(0, lumaPattern, 0, 0), null);

    private static H264SequenceParameterSet CreateMonochromeSps() =>
        new(0, 100, 40, 0, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264BitReader CreateReader(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1')
                bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}
