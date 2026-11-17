
using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacHuffmanDecoderTests
{
    private readonly AacHuffmanDecoder _decoder = new();

    [Fact]
    public void DecodeSymbol_ShouldDecodeSingleBitCodeword()
    {
        AacHuffmanCodebook codebook = CreateCodebook();
        AacBitReader reader = CreateReader("0");

        int result = _decoder.DecodeSymbol(reader, codebook);

        Assert.Equal(10, result);
        Assert.Equal(1, reader.PositionBits);
    }

    [Fact]
    public void DecodeSymbol_ShouldDecodeTwoBitCodeword()
    {
        AacHuffmanCodebook codebook = CreateCodebook();
        AacBitReader reader = CreateReader("10");

        int result = _decoder.DecodeSymbol(reader, codebook);

        Assert.Equal(20, result);
        Assert.Equal(2, reader.PositionBits);
    }

    [Fact]
    public void DecodeSymbol_ShouldDecodeOtherTwoBitCodeword()
    {
        AacHuffmanCodebook codebook = CreateCodebook();
        AacBitReader reader = CreateReader("11");

        int result = _decoder.DecodeSymbol(reader, codebook);

        Assert.Equal(30, result);
        Assert.Equal(2, reader.PositionBits);
    }

    [Fact]
    public void DecodeSymbol_ShouldDecodeConsecutiveSymbols()
    {
        AacHuffmanCodebook codebook = CreateCodebook();
        AacBitReader reader = CreateReader("01011");

        Assert.Equal(10, _decoder.DecodeSymbol(reader, codebook));
        Assert.Equal(20, _decoder.DecodeSymbol(reader, codebook));
        Assert.Equal(30, _decoder.DecodeSymbol(reader, codebook));
        Assert.Equal(5, reader.PositionBits);
    }

    [Fact]
    public void DecodeSymbol_ShouldThrowWhenCodewordIsTruncated()
    {
        AacHuffmanCodebook codebook = CreateCodebook();

        // The valid stream contains only the first bit of "10".
        AacBitReader reader = CreateReader("1");

        Assert.Throws<EndOfStreamException>(
            () => _decoder.DecodeSymbol(reader, codebook));
    }

    [Fact]
    public void DecodeSymbol_ShouldRejectInvalidCodeword()
    {
        AacHuffmanCodebook codebook = new(
        [
            new AacHuffmanCodeword(0b0, 1, 10),
            new AacHuffmanCodeword(0b10, 2, 20),
            new AacHuffmanCodeword(0b110, 3, 30)
        ]);

        AacBitReader reader = CreateReader("111");

        Assert.Throws<InvalidDataException>(
            () => _decoder.DecodeSymbol(reader, codebook));
    }

    [Fact]
    public void DecodeSymbol_ShouldRejectNullReader()
    {
        AacHuffmanCodebook codebook = CreateCodebook();

        Assert.Throws<ArgumentNullException>(
            () => _decoder.DecodeSymbol(null!, codebook));
    }

    [Fact]
    public void DecodeSymbol_ShouldRejectNullCodebook()
    {
        AacBitReader reader = CreateReader("0");

        Assert.Throws<ArgumentNullException>(
            () => _decoder.DecodeSymbol(reader, null!));
    }

    [Fact]
    public void Codebook_ShouldRejectEmptyCodewordCollection()
    {
        Assert.Throws<ArgumentException>(
            () => new AacHuffmanCodebook(
                Array.Empty<AacHuffmanCodeword>()));
    }

    [Fact]
    public void Codebook_ShouldRejectNullCodewordCollection()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AacHuffmanCodebook(null!));
    }

    [Fact]
    public void Codebook_ShouldRejectDuplicateCodewords()
    {
        AacHuffmanCodeword[] codewords =
        [
            new(0b0, 1, 10),
            new(0b0, 1, 20)
        ];

        Assert.Throws<ArgumentException>(
            () => new AacHuffmanCodebook(codewords));
    }

    [Fact]
    public void Codebook_ShouldRejectWhenExistingCodewordIsPrefix()
    {
        AacHuffmanCodeword[] codewords =
        [
            new(0b0, 1, 10),
            new(0b00, 2, 20)
        ];

        Assert.Throws<ArgumentException>(
            () => new AacHuffmanCodebook(codewords));
    }

    [Fact]
    public void Codebook_ShouldRejectWhenNewCodewordIsPrefix()
    {
        AacHuffmanCodeword[] codewords =
        [
            new(0b00, 2, 10),
            new(0b0, 1, 20)
        ];

        Assert.Throws<ArgumentException>(
            () => new AacHuffmanCodebook(codewords));
    }

    [Fact]
    public void Codebook_ShouldExposeReadOnlyCodewordCollection()
    {
        AacHuffmanCodebook codebook = CreateCodebook();

        Assert.Equal(3, codebook.Codewords.Count);
        Assert.Equal(10, codebook.Codewords[0].Symbol);
    }

    private static AacHuffmanCodebook CreateCodebook()
    {
        return new AacHuffmanCodebook(
        [
            new AacHuffmanCodeword(0b0, 1, 10),
            new AacHuffmanCodeword(0b10, 2, 20),
            new AacHuffmanCodeword(0b11, 2, 30)
        ]);
    }

    private static AacBitReader CreateReader(string bits)
    {
        byte[] data = new byte[(bits.Length + 7) / 8];

        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
            {
                data[i / 8] |= (byte)(1 << (7 - (i % 8)));
            }
            else if (bits[i] != '0')
            {
                throw new ArgumentException(
                    "Bit strings may only contain zeroes and ones.",
                    nameof(bits));
            }
        }

        return new AacBitReader(data, bits.Length);
    }
}
