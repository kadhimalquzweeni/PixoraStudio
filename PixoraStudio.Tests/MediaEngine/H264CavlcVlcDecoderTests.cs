using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CavlcVlcDecoderTests
{
    private readonly H264CavlcVlcDecoder _decoder = new();

    [Theory]
    [InlineData("1", 0, 0, 0)]
    [InlineData("01", 0, 1, 1)]
    [InlineData("11", 2, 0, 0)]
    [InlineData("1111", 4, 0, 0)]
    [InlineData("000011", 8, 0, 0)]
    public void DecodeCoeffToken_ShouldSelectContextTable(
        string code, int predictedNonZeroCount, int totalCoefficients, int trailingOnes)
    {
        H264CoeffToken token = _decoder.DecodeCoeffToken(CreateReader(code), predictedNonZeroCount);

        Assert.Equal(totalCoefficients, token.TotalCoefficients);
        Assert.Equal(trailingOnes, token.TrailingOnes);
    }

    [Fact]
    public void DecodeCoeffToken_ShouldDecodeChroma420DcTable()
    {
        H264CoeffToken token = _decoder.DecodeCoeffToken(CreateReader("000111"), 0, 4);

        Assert.Equal(1, token.TotalCoefficients);
        Assert.Equal(0, token.TrailingOnes);
    }

    [Fact]
    public void DecodeCoeffToken_ShouldDecodeChroma422DcTable()
    {
        H264CoeffToken token = _decoder.DecodeCoeffToken(CreateReader("1"), 0, 8);

        Assert.Equal(0, token.TotalCoefficients);
        Assert.Equal(0, token.TrailingOnes);
    }

    [Theory]
    [InlineData("1", 0)]
    [InlineData("011", 1)]
    [InlineData("010", 2)]
    public void DecodeTotalZeros_ShouldDecodeLumaTable(string code, int expected)
    {
        Assert.Equal(expected, _decoder.DecodeTotalZeros(CreateReader(code), 1));
    }

    [Fact]
    public void DecodeTotalZeros_ShouldDecodeChroma420DcTable()
    {
        Assert.Equal(3, _decoder.DecodeTotalZeros(CreateReader("000"), 1, 4));
    }

    [Fact]
    public void DecodeTotalZeros_ShouldDecodeChroma422DcTable()
    {
        Assert.Equal(7, _decoder.DecodeTotalZeros(CreateReader("00000"), 1, 8));
    }

    [Theory]
    [InlineData(1, "1", 0)]
    [InlineData(1, "0", 1)]
    [InlineData(2, "1", 0)]
    [InlineData(2, "01", 1)]
    [InlineData(2, "00", 2)]
    [InlineData(7, "000", 7)]
    public void DecodeRunBefore_ShouldUseZerosLeftTable(int zerosLeft, string code, int expected)
    {
        Assert.Equal(expected, _decoder.DecodeRunBefore(CreateReader(code), zerosLeft));
    }

    [Fact]
    public void DecodeCoeffToken_ShouldRejectTruncatedVlc()
    {
        Assert.Throws<EndOfStreamException>(() =>
            _decoder.DecodeCoeffToken(CreateReader("0"), 0));
    }

    [Fact]
    public void DecodeCoeffToken_ShouldRejectInvalidVlc()
    {
        // The all-zero prefix is not a valid codeword in the nC < 2 table.
        Assert.Throws<FormatException>(() =>
            _decoder.DecodeCoeffToken(CreateReader("0000000000000000"), 0));
    }

    [Fact]
    public void DecodeTotalZeros_ShouldRejectInvalidCoefficientCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _decoder.DecodeTotalZeros(CreateReader("1"), 0));
    }

    private static H264BitReader CreateReader(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
                bytes[i / 8] |= (byte)(1 << (7 - (i % 8)));
        }
        return new H264BitReader(bytes, bits.Length);
    }
}
