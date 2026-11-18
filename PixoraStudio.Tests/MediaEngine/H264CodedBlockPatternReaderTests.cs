using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CodedBlockPatternReaderTests
{
    [Theory]
    [InlineData(0, 47, 15, 2)]
    [InlineData(1, 31, 15, 1)]
    [InlineData(2, 15, 15, 0)]
    [InlineData(3, 0, 0, 0)]
    [InlineData(12, 39, 7, 2)]
    [InlineData(47, 41, 9, 2)]
    public void MapIntra_ShouldUse420422Table(
        uint codeNum, uint expectedPattern, uint expectedLuma, uint expectedChroma)
    {
        var result = H264CodedBlockPatternReader.MapIntra(codeNum, 1);

        Assert.Equal(expectedPattern, (result.Chroma << 4) | result.Luma);
        Assert.Equal(expectedLuma, result.Luma);
        Assert.Equal(expectedChroma, result.Chroma);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 0)]
    [InlineData(2, 7)]
    [InlineData(15, 9)]
    public void MapIntra_ShouldUseMonochromeAnd444Table(uint codeNum, uint expectedLuma)
    {
        var mono = H264CodedBlockPatternReader.MapIntra(codeNum, 0);
        var chroma444 = H264CodedBlockPatternReader.MapIntra(codeNum, 3);

        Assert.Equal(expectedLuma, mono.Luma);
        Assert.Equal(0u, mono.Chroma);
        Assert.Equal(expectedLuma, chroma444.Luma);
        Assert.Equal(0u, chroma444.Chroma);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void ReadIntra_ShouldReadUnsignedExpGolombAndMap(
        uint chromaArrayType, uint expectedCodeNum)
    {
        H264BitReader reader = CreateReader(WriteUe(expectedCodeNum));

        var result = new H264CodedBlockPatternReader().ReadIntra(reader, chromaArrayType);

        Assert.Equal(expectedCodeNum, result.CodeNum);
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void ReadIntra_ShouldRejectOutOfRangeCodeNumFor420()
    {
        H264BitReader reader = CreateReader(WriteUe(48));

        Assert.Throws<FormatException>(() =>
            new H264CodedBlockPatternReader().ReadIntra(reader, 1));
    }

    [Fact]
    public void ReadIntra_ShouldRejectOutOfRangeCodeNumForMonochrome()
    {
        H264BitReader reader = CreateReader(WriteUe(16));

        Assert.Throws<FormatException>(() =>
            new H264CodedBlockPatternReader().ReadIntra(reader, 0));
    }

    [Fact]
    public void MapIntra_ShouldRejectInvalidChromaArrayType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264CodedBlockPatternReader.MapIntra(0, 4));
    }

    [Fact]
    public void ReadIntra_ShouldRejectTruncatedCodeNum()
    {
        H264BitReader reader = CreateReader("000");

        Assert.Throws<EndOfStreamException>(() =>
            new H264CodedBlockPatternReader().ReadIntra(reader, 1));
    }

    private static string WriteUe(uint value)
    {
        uint codeNum = checked(value + 1);
        int leadingZeroes = 0;
        for (uint current = codeNum; current > 1; current >>= 1)
            leadingZeroes++;

        string suffix = Convert.ToString(codeNum, 2).PadLeft(leadingZeroes + 1, '0');
        return new string('0', leadingZeroes) + suffix;
    }

    private static H264BitReader CreateReader(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1')
                bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}
