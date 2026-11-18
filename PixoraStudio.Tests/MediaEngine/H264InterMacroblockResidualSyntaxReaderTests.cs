using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InterMacroblockResidualSyntaxReaderTests
{
    private readonly H264InterMacroblockResidualSyntaxReader _reader = new();

    [Fact]
    public void Read_WithZeroCodedBlockPattern_ShouldNotReadQpDelta()
    {
        H264BitReader bits = Bits(WriteUe(0));
        H264InterMacroblockResidualSyntax result =
            _reader.Read(bits, Header(), Sps(), Pps());

        Assert.Equal(0u, result.CodedBlockPattern.Luma);
        Assert.Equal(0u, result.CodedBlockPattern.Chroma);
        Assert.Null(result.MacroblockQpDelta);
        Assert.Equal(0, bits.RemainingBits);
    }

    [Theory]
    [InlineData(2, 1, 0, 1)]
    [InlineData(1, 0, 1, -1)]
    [InlineData(12, 15, 2, 1)]
    public void Read_WithCodedBlocks_ShouldReadSignedQpDelta(
        uint cbpCodeNum, uint expectedLuma, uint expectedChroma, int qpDelta)
    {
        string encoded = WriteUe(cbpCodeNum) + WriteSe(qpDelta);
        H264BitReader bits = Bits(encoded);

        H264InterMacroblockResidualSyntax result =
            _reader.Read(bits, Header(), Sps(), Pps());

        Assert.Equal(expectedLuma, result.CodedBlockPattern.Luma);
        Assert.Equal(expectedChroma, result.CodedBlockPattern.Chroma);
        Assert.Equal(qpDelta, result.MacroblockQpDelta);
        Assert.Equal(0, bits.RemainingBits);
    }

    [Fact]
    public void Read_ShouldRejectInvalidMacroblockAndUnsupportedConfiguration()
    {
        Assert.Throws<ArgumentException>(() =>
            _reader.Read(Bits(WriteUe(0)), Header(isIntra: true), Sps(), Pps()));
        Assert.Throws<NotSupportedException>(() =>
            _reader.Read(Bits(WriteUe(0)), Header(), Sps(), Pps() with { EntropyCodingModeFlag = true }));
        Assert.Throws<NotSupportedException>(() =>
            _reader.Read(Bits(WriteUe(0)), Header(), Sps() with { FrameMbsOnlyFlag = false }, Pps()));
        Assert.Throws<ArgumentException>(() =>
            _reader.Read(Bits(WriteUe(0)), Header(), Sps(), Pps() with { SequenceParameterSetId = 1 }));
    }

    private static H264MacroblockHeader Header(bool isIntra = false) =>
        new(0, 0, false, 0, isIntra, false, null, null, null, false);

    private static H264SequenceParameterSet Sps() =>
        new(0, 100, 40, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader Pps() =>
        new(0, 0, false, false, 0);

    private static string WriteUe(uint value)
    {
        uint codeNum = checked(value + 1);
        int leadingZeroes = 0;
        for (uint current = codeNum; current > 1; current >>= 1)
            leadingZeroes++;
        return new string('0', leadingZeroes) +
            Convert.ToString(codeNum, 2).PadLeft(leadingZeroes + 1, '0');
    }

    private static string WriteSe(int value) =>
        WriteUe(value <= 0 ? checked((uint)(-2L * value)) : checked((uint)(2L * value - 1)));

    private static H264BitReader Bits(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1')
                bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}
