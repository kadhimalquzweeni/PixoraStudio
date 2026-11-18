using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CavlcNeighbourContextTests
{
    [Fact]
    public void GetPredictedNonZeroCount_ShouldReturnZeroWhenNoNeighboursAreAvailable()
    {
        var context = new H264CavlcNeighbourContext();

        Assert.Equal(0, context.GetPredictedNonZeroCount(0, 0, 1));
        Assert.Equal(0, context.GetPredictedNonZeroCount(3, 3, 1));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldUseLeftNeighbourWhenTopIsUnavailable()
    {
        var context = new H264CavlcNeighbourContext();
        context.RecordBlock(0, 1, 4, 5);

        Assert.Equal(5, context.GetPredictedNonZeroCount(1, 1, 4));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldUseTopNeighbourWhenLeftIsUnavailable()
    {
        var context = new H264CavlcNeighbourContext();
        context.RecordBlock(1, 0, 4, 7);

        Assert.Equal(7, context.GetPredictedNonZeroCount(1, 1, 4));
    }

    [Theory]
    [InlineData(2, 5, 4)]
    [InlineData(3, 4, 4)]
    [InlineData(0, 1, 1)]
    public void GetPredictedNonZeroCount_ShouldAverageBothNeighbours(
        int left, int top, int expected)
    {
        var context = new H264CavlcNeighbourContext();
        context.RecordBlock(0, 1, 2, left);
        context.RecordBlock(1, 0, 2, top);

        Assert.Equal(expected, context.GetPredictedNonZeroCount(1, 1, 2));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldTreatDifferentSliceAsUnavailable()
    {
        var context = new H264CavlcNeighbourContext();
        context.RecordBlock(0, 1, 10, 8);
        context.RecordBlock(1, 0, 11, 4);

        Assert.Equal(8, context.GetPredictedNonZeroCount(1, 1, 10));
        Assert.Equal(4, context.GetPredictedNonZeroCount(1, 1, 11));
        Assert.Equal(0, context.GetPredictedNonZeroCount(1, 1, 12));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldTreatUndecodedNeighbourAsUnavailable()
    {
        var context = new H264CavlcNeighbourContext();
        context.RecordBlock(1, 0, 1, 6);

        Assert.Equal(6, context.GetPredictedNonZeroCount(1, 1, 1));
    }

    [Fact]
    public void ReadBlock_ShouldDecodeUsingDerivedContextAndRecordTotalCoeff()
    {
        var context = new H264CavlcNeighbourContext();
        context.RecordBlock(0, 1, 1, 2);
        context.RecordBlock(1, 0, 1, 4);
        context.RecordBlock(2, 0, 1, 4);

        // nC=(2+4+1)>>1 = 3. In coeff_token table 1, 011 is
        // TotalCoeff=2/TrailingOnes=2; signs are positive and total_zeros=0.
        H264BitReader reader = CreateReader("01100111");

        H264CavlcResidualBlock result = context.ReadBlock(reader, 1, 1, 1);

        Assert.Equal(2, result.TotalCoefficients);
        Assert.Equal(3, context.GetPredictedNonZeroCount(2, 1, 1));
    }

    [Fact]
    public void Reset_ShouldClearRecordedBlocks()
    {
        var context = new H264CavlcNeighbourContext();
        context.RecordBlock(0, 1, 1, 9);
        context.RecordBlock(1, 0, 1, 7);

        context.Reset();

        Assert.Equal(0, context.GetPredictedNonZeroCount(1, 1, 1));
    }

    [Fact]
    public void RecordBlock_ShouldRejectInvalidValues()
    {
        var context = new H264CavlcNeighbourContext();

        Assert.Throws<ArgumentOutOfRangeException>(() => context.RecordBlock(-1, 0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.RecordBlock(0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.RecordBlock(0, 0, 1, 17));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldRejectNegativeCoordinates()
    {
        var context = new H264CavlcNeighbourContext();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            context.GetPredictedNonZeroCount(-1, 0, 1));
    }

    private static H264BitReader CreateReader(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1') bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}
