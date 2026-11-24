using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacSpectralSectionDecoderTests
{
    private readonly AacSpectralSectionDecoder _decoder = new();

    [Fact]
    public void Decode_ShouldDecodeLongWindowSpectralBands()
    {
        // Two 4-coefficient codebook-1 bands, each all-zero tuple.
        var reader = new AacBitReader(new byte[] { 0b0000_0000 }, 2);
        var result = _decoder.Decode(
            reader,
            [Section(0, 0, 2, 1)],
            [0, 4, 8],
            [1]);

        Assert.Equal(2, result.Count);
        Assert.Equal(new int[4], result[0].Coefficients);
        Assert.Equal(new int[4], result[1].Coefficients);
        Assert.Equal(2, reader.PositionBits);
    }

    [Fact]
    public void Decode_ShouldMultiplyShortBandWidthByWindowGroupLength()
    {
        // Two windows in one group: 4 coefficients per window, 8 total.
        var reader = new AacBitReader(new byte[] { 0b0000_0000 }, 2);
        var result = _decoder.Decode(
            reader,
            [Section(0, 0, 1, 1)],
            [0, 4],
            [2]);

        var band = Assert.Single(result);
        Assert.Equal(8, band.Coefficients.Length);
        Assert.Equal(2, reader.PositionBits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void Decode_ShouldNotConsumeSpectralBitsForNonSpectralBooks(int codebook)
    {
        var reader = new AacBitReader(Array.Empty<byte>());
        var result = _decoder.Decode(
            reader,
            [Section(0, 0, 1, codebook)],
            [0, 4],
            [1]);

        Assert.Equal(new int[4], Assert.Single(result).Coefficients);
        Assert.Equal(0, reader.PositionBits);
    }

    [Fact]
    public void Decode_ShouldRejectReservedCodebook()
    {
        Assert.Throws<InvalidDataException>(() =>
            _decoder.Decode(
                new AacBitReader(Array.Empty<byte>()),
                [Section(0, 0, 1, 12)],
                [0, 4],
                [1]));
    }

    [Fact]
    public void Decode_ShouldRejectMissingWindowGroup()
    {
        Assert.Throws<InvalidDataException>(() =>
            _decoder.Decode(
                new AacBitReader(Array.Empty<byte>()),
                [Section(1, 0, 1, 0)],
                [0, 4],
                [1]));
    }

    [Fact]
    public void Decode_ShouldRejectInvalidOffsetsAndArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _decoder.Decode(null!, Array.Empty<AacSectionInfo>(), [0, 4], [1]));
        Assert.Throws<ArgumentNullException>(() =>
            _decoder.Decode(new AacBitReader(Array.Empty<byte>()), null!, [0, 4], [1]));
        Assert.Throws<ArgumentException>(() =>
            _decoder.Decode(new AacBitReader(Array.Empty<byte>()), [], [0], [1]));
        Assert.Throws<ArgumentException>(() =>
            _decoder.Decode(new AacBitReader(Array.Empty<byte>()), [], [0, 4, 3], [1]));
        Assert.Throws<ArgumentException>(() =>
            _decoder.Decode(new AacBitReader(Array.Empty<byte>()), [], [0, 4], [0]));
    }

    private static AacSectionInfo Section(int group, int start, int count, int book) =>
        new(group, start, count, book);
}
