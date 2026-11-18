using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraMacroblockSyntaxReaderTests
{
    [Fact]
    public void Read_IntraNxN_ShouldParseTransformModesChromaPatternAndQpDelta()
    {
        // transform8x8=1; four prev_mode_flag=1; chroma mode=2;
        // coded_block_pattern codeNum=0 maps to luma=15/chroma=2; mb_qp_delta=-2.
        string bits = "1" + "1111" + WriteUe(2) + WriteUe(0) + WriteSe(-2);
        H264BitReader reader = CreateReader(bits);

        H264IntraMacroblockSyntax result = new H264IntraMacroblockSyntaxReader().Read(
            reader,
            CreateIntraNxNHeader(),
            CreateSps(chromaFormat: 1),
            CreatePps(transform8x8: true));

        Assert.True(result.TransformSize8X8Flag);
        Assert.NotNull(result.LumaPredictionModes);
        Assert.True(result.LumaPredictionModes.Uses8X8Transform);
        Assert.Equal(4, result.LumaPredictionModes.Modes.Count);
        Assert.Equal(2u, result.IntraChromaPredictionMode);
        Assert.Equal(15u, result.CodedBlockPattern.Luma);
        Assert.Equal(2u, result.CodedBlockPattern.Chroma);
        Assert.Equal(-2, result.MacroblockQpDelta);
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void Read_IntraNxNWithoutTransformFlag_ShouldReadSixteen4x4Modes()
    {
        string bits = string.Concat(Enumerable.Repeat("1", 16)) +
            WriteUe(0) + WriteUe(3);
        H264BitReader reader = CreateReader(bits);

        var result = new H264IntraMacroblockSyntaxReader().Read(
            reader,
            CreateIntraNxNHeader(),
            CreateSps(chromaFormat: 1),
            CreatePps(transform8x8: false));

        Assert.Null(result.TransformSize8X8Flag);
        Assert.Equal(16, result.LumaPredictionModes!.Modes.Count);
        Assert.Equal(0u, result.CodedBlockPattern.Luma);
        Assert.Equal(0u, result.CodedBlockPattern.Chroma);
        Assert.Null(result.MacroblockQpDelta);
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void Read_MonochromeIntraNxN_ShouldOmitChromaModeAndUseMonochromeTable()
    {
        string bits = string.Concat(Enumerable.Repeat("1", 16)) + WriteUe(0) + WriteSe(1);
        H264BitReader reader = CreateReader(bits);

        var result = new H264IntraMacroblockSyntaxReader().Read(
            reader,
            CreateIntraNxNHeader(),
            CreateSps(chromaFormat: 0),
            CreatePps(transform8x8: false));

        Assert.Null(result.IntraChromaPredictionMode);
        Assert.Equal(15u, result.CodedBlockPattern.Luma);
        Assert.Equal(0u, result.CodedBlockPattern.Chroma);
        Assert.Equal(1, result.MacroblockQpDelta);
    }

    [Fact]
    public void Read_Intra16x16_ShouldUsePatternFromMacroblockTypeAndAlwaysReadQpDelta()
    {
        string bits = WriteUe(1) + WriteSe(-1);
        H264BitReader reader = CreateReader(bits);
        H264MacroblockHeader header = new(
            0, 1, false, 0, true, true, 0, 0, 0, false);

        var result = new H264IntraMacroblockSyntaxReader().Read(
            reader, header, CreateSps(1), CreatePps(false));

        Assert.Null(result.TransformSize8X8Flag);
        Assert.Null(result.LumaPredictionModes);
        Assert.Equal(1u, result.IntraChromaPredictionMode);
        Assert.Equal(0u, result.CodedBlockPattern.Luma);
        Assert.Equal(0u, result.CodedBlockPattern.Chroma);
        Assert.Equal(-1, result.MacroblockQpDelta);
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void Read_SeparateColourPlane_ShouldNotReadChromaPredictionMode()
    {
        string bits = string.Concat(Enumerable.Repeat("1", 16)) + WriteUe(3);
        H264BitReader reader = CreateReader(bits);
        H264SequenceParameterSet sps = CreateSps(3) with { SeparateColourPlaneFlag = true };

        var result = new H264IntraMacroblockSyntaxReader().Read(
            reader, CreateIntraNxNHeader(), sps, CreatePps(false));

        Assert.Null(result.IntraChromaPredictionMode);
        Assert.Equal(0u, result.CodedBlockPattern.Chroma);
        Assert.Null(result.MacroblockQpDelta);
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void Read_ShouldRejectInvalidChromaPredictionMode()
    {
        H264BitReader reader = CreateReader(
            string.Concat(Enumerable.Repeat("1", 16)) + WriteUe(4));

        Assert.Throws<FormatException>(() =>
            new H264IntraMacroblockSyntaxReader().Read(
                reader, CreateIntraNxNHeader(), CreateSps(1), CreatePps(false)));
    }

    [Fact]
    public void Read_ShouldRejectInterMacroblock()
    {
        H264MacroblockHeader inter = new(0, 0, false, 0, false, false, null, null, null, false);

        Assert.Throws<ArgumentException>(() =>
            new H264IntraMacroblockSyntaxReader().Read(
                CreateReader("1"), inter, CreateSps(1), CreatePps(false)));
    }

    [Fact]
    public void Read_ShouldRejectCabac()
    {
        Assert.Throws<NotSupportedException>(() =>
            new H264IntraMacroblockSyntaxReader().Read(
                CreateReader("1"), CreateIntraNxNHeader(), CreateSps(1), CreatePps(false) with
                { EntropyCodingModeFlag = true }));
    }

    [Fact]
    public void Read_ShouldRejectPpsThatReferencesAnotherSps()
    {
        Assert.Throws<ArgumentException>(() =>
            new H264IntraMacroblockSyntaxReader().Read(
                CreateReader("1"), CreateIntraNxNHeader(), CreateSps(1),
                CreatePps(false) with { SequenceParameterSetId = 1 }));
    }

    private static H264MacroblockHeader CreateIntraNxNHeader() =>
        new(0, 0, false, 0, true, false, null, null, null, false);

    private static H264SequenceParameterSet CreateSps(uint chromaFormat) =>
        new(0, 100, 40, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader CreatePps(bool transform8x8) =>
        new(0, 0, false, false, 0) { Transform8X8ModeFlag = transform8x8 };

    private static string WriteUe(uint value)
    {
        uint codeNum = checked(value + 1);
        int leadingZeroes = 0;
        for (uint current = codeNum; current > 1; current >>= 1) leadingZeroes++;
        return new string('0', leadingZeroes) +
            Convert.ToString(codeNum, 2).PadLeft(leadingZeroes + 1, '0');
    }

    private static string WriteSe(int value)
    {
        uint codeNum = value <= 0
            ? checked((uint)(-2L * value))
            : checked((uint)(2L * value - 1));
        return WriteUe(codeNum);
    }

    private static H264BitReader CreateReader(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1') bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}
