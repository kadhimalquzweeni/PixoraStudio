using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CavlcResidualBlockReaderTests
{
    private readonly H264CavlcResidualBlockReader _reader = new();

    [Fact]
    public void ReadBlock_ShouldReturnZeroBlock_WhenCoeffTokenHasNoCoefficients()
    {
        H264CavlcResidualBlock block = _reader.ReadBlock(CreateReader("1"), 0);

        Assert.Equal(0, block.TotalCoefficients);
        Assert.Equal(0, block.TotalZeros);
        Assert.All(block.Coefficients, value => Assert.Equal(0, value));
    }

    [Fact]
    public void ReadBlock_ShouldDecodeSinglePositiveTrailingOne()
    {
        // coeff_token: TotalCoeff=1, TrailingOnes=1; sign=0; total_zeros=0.
        H264CavlcResidualBlock block = _reader.ReadBlock(CreateReader("0101"), 0);

        Assert.Equal(1, block.TotalCoefficients);
        Assert.Equal(1, block.TrailingOnes);
        Assert.Equal(0, block.TotalZeros);
        Assert.Equal(1, block.Coefficients[0]);
        Assert.Equal(0, block.Coefficients.Skip(1).Sum(Math.Abs));
    }

    [Fact]
    public void ReadBlock_ShouldDecodeNegativeTrailingOne()
    {
        H264CavlcResidualBlock block = _reader.ReadBlock(CreateReader("0111"), 0);

        Assert.Equal(-1, block.Coefficients[0]);
    }

    [Fact]
    public void ReadBlock_ShouldDecodeTwoTrailingOnesInReverseScanOrder()
    {
        // coeff_token (TotalCoeff=2, TrailingOnes=2), signs +,+, total_zeros=0.
        H264CavlcResidualBlock block = _reader.ReadBlock(CreateReader("00100111"), 0);

        Assert.Equal(2, block.TotalCoefficients);
        Assert.Equal(2, block.TrailingOnes);
        Assert.Equal(new[] { 1, 1 }, block.Coefficients.Take(2));
    }

    [Fact]
    public void ReadBlock_ShouldDecodeFirstNonTrailingLevelAdjustment()
    {
        // coeff_token TotalCoeff=1/TrailingOnes=0, level_prefix=0, total_zeros=0.
        H264CavlcResidualBlock block = _reader.ReadBlock(CreateReader("00010111"), 0);

        Assert.Equal(2, block.Coefficients[0]);
    }

    [Fact]
    public void ReadBlock_ShouldPlaceCoefficientAfterDecodedZeroRun()
    {
        // coeff_token TotalCoeff=1/TrailingOnes=1, sign positive, total_zeros=1.
        // For TotalCoeff=1 the total_zeros code "011" represents one zero.
        H264CavlcResidualBlock block = _reader.ReadBlock(CreateReader("010011"), 0);

        Assert.Equal(1, block.TotalZeros);
        Assert.Equal(0, block.Coefficients[0]);
        Assert.Equal(1, block.Coefficients[1]);
    }

    [Fact]
    public void ReadBlock_ShouldRejectTruncatedTrailingOneSign()
    {
        Assert.Throws<EndOfStreamException>(() =>
            _reader.ReadBlock(CreateReader("01"), 0));
    }

    [Fact]
    public void ReadBlock_ShouldRejectTruncatedLevelPrefix()
    {
        // Valid coeff_token for one non-trailing coefficient, followed by an unterminated level prefix.
        Assert.Throws<EndOfStreamException>(() =>
            _reader.ReadBlock(CreateReader("0001010"), 0));
    }

    [Fact]
    public void ReadBlock_ShouldRejectUnsupportedBlockSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _reader.ReadBlock(CreateReader("1"), 0, 12));
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
