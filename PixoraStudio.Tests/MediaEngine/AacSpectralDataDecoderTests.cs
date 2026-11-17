using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacSpectralDataDecoderTests
{
    private readonly AacSpectralDataDecoder _decoder = new();

    [Fact]
    public void DecodeBand_ShouldDecodeZeroCodebookWithoutConsumingBits()
    {
        var reader = new AacBitReader(new byte[] { 0xA0 }, 3);
        int[] result = _decoder.DecodeBand(reader, 0, 4);
        Assert.Equal(new int[4], result);
        Assert.Equal(0, reader.PositionBits);
    }

    [Fact]
    public void DecodeBand_ShouldDecodeNormativeBookOneZeroTuple()
    {
        // Book 1: symbol 40 (0,0,0,0) has codeword 0 with length 1.
        var reader = new AacBitReader(new byte[] { 0x00 }, 1);
        int[] result = _decoder.DecodeBand(reader, 1, 4);
        Assert.Equal(new[] { 0, 0, 0, 0 }, result);
        Assert.Equal(1, reader.PositionBits);
    }

    [Fact]
    public void DecodeBand_ShouldDecodeUnsignedSignsInCoefficientOrder()
    {
        // Book 7 symbol 1 -> (0,1), followed by a negative sign bit.
        var reader = new AacBitReader(new byte[] { 0b0010_0000 }, 2);
        int[] result = _decoder.DecodeBand(reader, 7, 2);
        Assert.Equal(new[] { 0, -1 }, result);
        Assert.Equal(2, reader.PositionBits);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(5, 3)]
    [InlineData(7, 3)]
    public void DecodeBand_ShouldRejectCoefficientCountNotDivisibleByTupleSize(
        int codebook, int count)
    {
        Assert.Throws<ArgumentException>(
            () => _decoder.DecodeBand(new AacBitReader(Array.Empty<byte>()), codebook, count));
    }

    [Fact]
    public void DecodeBand_ShouldRejectNegativeCoefficientCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _decoder.DecodeBand(new AacBitReader(Array.Empty<byte>()), 1, -1));
    }

    [Fact]
    public void DecodeBand_ShouldRejectNullReader()
    {
        Assert.Throws<ArgumentNullException>(
            () => _decoder.DecodeBand(null!, 1, 4));
    }
}
