using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacSpectralTupleDecoderTests
{
    private readonly AacSpectralTupleDecoder _decoder = new();

    [Theory]
    [InlineData(1, 0, -1, -1, -1, -1)]
    [InlineData(1, 40, 0, 0, 0, 0)]
    [InlineData(1, 80, 1, 1, 1, 1)]
    [InlineData(2, 40, 0, 0, 0, 0)]
    public void DecodeTupleFromIndex_ShouldDecodeSignedQuadruples(
        int codebook,
        int index,
        int a,
        int b,
        int c,
        int d)
    {
        int[] result = _decoder.DecodeTupleFromIndex(
            codebook,
            index);

        Assert.Equal([a, b, c, d], result);
    }

    [Theory]
    [InlineData(3, 0, 0, 0, 0, 0)]
    [InlineData(3, 80, 2, 2, 2, 2)]
    [InlineData(4, 40, 1, 1, 1, 1)]
    public void DecodeTupleFromIndex_ShouldDecodeUnsignedQuadruples(
        int codebook,
        int index,
        int a,
        int b,
        int c,
        int d)
    {
        int[] result = _decoder.DecodeTupleFromIndex(
            codebook,
            index);

        Assert.Equal([a, b, c, d], result);
    }

    [Theory]
    [InlineData(5, 0, -4, -4)]
    [InlineData(5, 44, 4, 0)]
    [InlineData(5, 45, -4, 1)]
    [InlineData(5, 40, 0, 0)]
    [InlineData(5, 80, 4, 4)]
    [InlineData(6, 40, 0, 0)]
    public void DecodeTupleFromIndex_ShouldDecodeSignedPairs(
        int codebook,
        int index,
        int a,
        int b)
    {
        int[] result = _decoder.DecodeTupleFromIndex(
            codebook,
            index);

        Assert.Equal([a, b], result);
    }

    [Theory]
    [InlineData(7, 0, 0, 0)]
    [InlineData(7, 1, 1, 0)]
    [InlineData(7, 8, 0, 1)]
    [InlineData(7, 63, 7, 7)]
    [InlineData(8, 63, 7, 7)]
    [InlineData(9, 168, 12, 12)]
    [InlineData(10, 168, 12, 12)]
    [InlineData(11, 288, 16, 16)]
    public void DecodeTupleFromIndex_ShouldDecodeUnsignedPairs(
        int codebook,
        int index,
        int a,
        int b)
    {
        int[] result = _decoder.DecodeTupleFromIndex(
            codebook,
            index);

        Assert.Equal([a, b], result);
    }


    [Theory]
    [InlineData(5, 0, -4, -4)]
    [InlineData(5, 8, 4, -4)]
    [InlineData(5, 9, -4, -3)]
    [InlineData(6, 80, 4, 4)]
    [InlineData(7, 1, 1, 0)]
    [InlineData(8, 8, 0, 1)]
    [InlineData(9, 13, 0, 1)]
    [InlineData(10, 168, 12, 12)]
    [InlineData(11, 17, 0, 1)]
    public void DecodeTupleFromIndex_ShouldFollowNormativePairIndexOrdering(
        int codebook,
        int index,
        int first,
        int second)
    {
        int[] result = _decoder.DecodeTupleFromIndex(codebook, index);

        Assert.Equal([first, second], result);
    }

    [Theory]
    [InlineData(5, 9, -4)]
    [InlineData(6, 9, -4)]
    [InlineData(7, 8, 0)]
    [InlineData(8, 8, 0)]
    [InlineData(9, 13, 0)]
    [InlineData(10, 13, 0)]
    [InlineData(11, 17, 0)]
    public void DecodeTupleFromIndex_ShouldExhaustivelyFollowNormativePairOrdering(
        int codebook,
        int radix,
        int offset)
    {
        for (int index = 0; index < radix * radix; index++)
        {
            int[] result = _decoder.DecodeTupleFromIndex(codebook, index);

            Assert.Equal(index % radix + offset, result[0]);
            Assert.Equal(index / radix + offset, result[1]);
        }
    }

    [Fact]
    public void DecodeTupleFromIndex_ShouldRejectZeroCodebook()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _decoder.DecodeTupleFromIndex(0, 0));
    }

    [Theory]
    [InlineData(1, 81)]
    [InlineData(3, 81)]
    [InlineData(5, 81)]
    [InlineData(7, 64)]
    [InlineData(9, 169)]
    [InlineData(11, 289)]
    public void DecodeTupleFromIndex_ShouldRejectOutOfRangeIndex(
        int codebook,
        int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _decoder.DecodeTupleFromIndex(codebook, index));
    }

    [Fact]
    public void DecodeTuple_ShouldApplyUnsignedSignBits()
    {
        // Codebook 7, index 1 produces (1, 0).
        // The first coefficient's sign bit is negative.
        AacBitReader reader = CreateReader("1");

        int[] result = _decoder.DecodeTuple(
            reader,
            7,
            1);

        Assert.Equal([-1, 0], result);
        Assert.Equal(1, reader.PositionBits);
    }

    [Fact]
    public void DecodeTuple_ShouldApplySignsToEveryNonZeroCoefficient()
    {
        // Codebook 7, index 9 produces (1, 1).
        AacBitReader reader = CreateReader("10");

        int[] result = _decoder.DecodeTuple(
            reader,
            7,
            9);

        Assert.Equal([-1, 1], result);
        Assert.Equal(2, reader.PositionBits);
    }

    [Fact]
    public void DecodeTuple_ShouldDecodeEscapeMagnitude()
    {
        // Codebook 11, index 16 produces (16, 0).
        // Sign = negative.
        // Escape: terminating zero followed by 0011.
        // Magnitude = 2^4 + 3 = 19.
        AacBitReader reader = CreateReader("100011");

        int[] result = _decoder.DecodeTuple(
            reader,
            11,
            16);

        Assert.Equal([-19, 0], result);
        Assert.Equal(6, reader.PositionBits);
    }

    [Fact]
    public void DecodeTuple_ShouldDecodeMaximumEscapeMagnitude()
    {
        // Escape length 12:
        // eight unary ones, terminator zero, twelve value bits.
        string bits = "1" + "111111110" + "111111111111";

        AacBitReader reader = CreateReader(bits);

        int[] result = _decoder.DecodeTuple(
            reader,
            11,
            16);

        Assert.Equal([0, -8191], result);
    }

    [Fact]
    public void DecodeTuple_ShouldRejectEscapeLengthAboveLimit()
    {
        // Sign bit followed by nine unary ones.
        AacBitReader reader = CreateReader("1111111111");

        Assert.Throws<InvalidDataException>(
            () => _decoder.DecodeTuple(reader, 11, 16));
    }

    [Fact]
    public void DecodeTuple_ShouldThrowWhenSignBitIsTruncated()
    {
        AacBitReader reader = new(Array.Empty<byte>());

        Assert.Throws<EndOfStreamException>(
            () => _decoder.DecodeTuple(reader, 7, 1));
    }

    [Fact]
    public void DecodeTuple_ShouldThrowWhenEscapeDataIsTruncated()
    {
        // Sign bit: 1 (negative)
        // Six unary ones followed by the terminating zero.
        // This produces an escape length of 10 bits,
        // but no bits remain for the escape value.
        AacBitReader reader = CreateReader("11111110");

        Assert.Throws<EndOfStreamException>(
            () => _decoder.DecodeTuple(reader, 11, 16));
    }

    [Fact]
    public void DecodeEscapeValue_ShouldRejectNullReader()
    {
        Assert.Throws<ArgumentNullException>(
            () => _decoder.DecodeEscapeValue(null!));
    }

    private static AacBitReader CreateReader(string bits)
    {
        byte[] data = new byte[(bits.Length + 7) / 8];

        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
            {
                data[i / 8] |= (byte)(1 << (7 - i % 8));
            }
            else if (bits[i] != '0')
            {
                throw new ArgumentException(
                    "Bit strings may only contain zeroes and ones.",
                    nameof(bits));
            }
        }

        return new AacBitReader(data);
    }
}