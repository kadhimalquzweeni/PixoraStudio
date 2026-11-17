using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacHuffmanCodebookFactoryTests
{
    [Fact]
    public void CreateScaleFactorCodebook_ShouldContainAll121NormativeEntries()
    {
        var book = AacHuffmanCodebookFactory.CreateScaleFactorCodebook();
        Assert.Equal(121, book.Codewords.Count);
        Assert.Contains(book.Codewords, x => x.Symbol == -60);
        Assert.Contains(book.Codewords, x => x.Symbol == 0 && x.BitLength == 1 && x.Codeword == 0);
        Assert.Contains(book.Codewords, x => x.Symbol == 60);
    }

    [Theory]
    [InlineData(1, 81)]
    [InlineData(2, 81)]
    [InlineData(3, 81)]
    [InlineData(4, 81)]
    [InlineData(5, 81)]
    [InlineData(6, 81)]
    [InlineData(7, 64)]
    [InlineData(8, 64)]
    [InlineData(9, 169)]
    [InlineData(10, 169)]
    [InlineData(11, 289)]
    public void CreateSpectralCodebook_ShouldContainNormativeEntryCount(int index, int expected)
    {
        var book = AacHuffmanCodebookFactory.CreateSpectralCodebook(index);
        Assert.Equal(expected, book.Codewords.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(15)]
    public void CreateSpectralCodebook_ShouldRejectInvalidIndex(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AacHuffmanCodebookFactory.CreateSpectralCodebook(index));
    }

    [Fact]
    public void SpectralCodebook1_ShouldDecodeItsZeroTuple()
    {
        // Codebook 1 symbol 40 is the all-zero tuple, encoded as a single zero bit.
        var reader = new AacBitReader(new byte[] { 0x00 }, 1);
        int symbol = new AacHuffmanDecoder().DecodeSymbol(
            reader, AacHuffmanCodebookFactory.CreateSpectralCodebook(1));
        Assert.Equal(40, symbol);
        Assert.Equal(1, reader.PositionBits);
    }
}
