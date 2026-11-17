using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacHuffmanCodebookTests
{
    [Fact]
    public void Constructor_ShouldAcceptValidPrefixFreeCodewords()
    {
        AacHuffmanCodeword[] entries =
        [
            new(0b0, 1, 10),
            new(0b10, 2, 20),
            new(0b11, 2, 30)
        ];

        AacHuffmanCodebook result =
            new(entries);

        Assert.Equal(3, result.Codewords.Count);
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyCodebook()
    {
        Assert.Throws<ArgumentException>(
            () => new AacHuffmanCodebook(
                Array.Empty<AacHuffmanCodeword>()));
    }

    [Fact]
    public void Constructor_ShouldRejectDuplicateCodewords()
    {
        AacHuffmanCodeword[] entries =
        [
            new(0b0, 1, 10),
            new(0b0, 1, 20)
        ];

        Assert.Throws<ArgumentException>(
            () => new AacHuffmanCodebook(entries));
    }

    [Fact]
    public void Constructor_ShouldRejectPrefixConflicts()
    {
        AacHuffmanCodeword[] entries =
        [
            new(0b0, 1, 10),
            new(0b01, 2, 20)
        ];

        Assert.Throws<ArgumentException>(
            () => new AacHuffmanCodebook(entries));
    }

    [Fact]
    public void Constructor_ShouldRejectCodewordThatDoesNotFit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AacHuffmanCodeword(0b100, 2, 10));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0b10, 2)]
    [InlineData(0b11, 2)]
    public void Decoder_ShouldDecodeSymbols(
    int bits,
    int bitLength)
    {
        AacHuffmanCodebook codebook = new(
        [
            new(0b0, 1, 10),
        new(0b10, 2, 20),
        new(0b11, 2, 30)
        ]);

        byte[] data = bitLength switch
        {
            1 => [(byte)(bits << 7)],
            2 => [(byte)(bits << 6)],
            _ => throw new ArgumentOutOfRangeException()
        };

        AacBitReader reader = new(data);
        AacHuffmanDecoder decoder = new();

        int result = decoder.DecodeSymbol(reader, codebook);

        int expected = bits switch
        {
            0 => 10,
            2 => 20,
            3 => 30,
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(expected, result);
        Assert.Equal(bitLength, reader.PositionBits);
    }

    [Fact]
    public void Decoder_ShouldRejectInvalidCodeword()
    {
        AacHuffmanCodebook codebook = new(
        [
            new(0b0, 1, 10),
            new(0b10, 2, 20)
        ]);

        AacBitReader reader = new(
            new byte[] { 0b1100_0000 });

        AacHuffmanDecoder decoder = new();

        Assert.Throws<InvalidDataException>(
            () => decoder.DecodeSymbol(reader, codebook));
    }

    [Fact]
    public void Decoder_ShouldThrowWhenInputIsTruncated()
    {
        AacHuffmanCodebook codebook = new(
        [
            new(0b10, 2, 10)
        ]);

        AacBitReader reader = new(Array.Empty<byte>());
        AacHuffmanDecoder decoder = new();

        Assert.Throws<EndOfStreamException>(
            () => decoder.DecodeSymbol(reader, codebook));
    }
}