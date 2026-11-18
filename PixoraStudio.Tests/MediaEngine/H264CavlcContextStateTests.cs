using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CavlcContextStateTests
{
    [Fact]
    public void GetPredictedNonZeroCount_ShouldReturnZero_WhenNoNeighboursAreAvailable()
    {
        H264CavlcContextState state = new(4, 4);

        Assert.Equal(0, state.GetPredictedNonZeroCount(0, 0, 0));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldUseLeftNeighbour_WhenTopIsUnavailable()
    {
        H264CavlcContextState state = new(4, 4);
        state.SetDecodedBlock(0, 1, 7, 5);

        Assert.Equal(5, state.GetPredictedNonZeroCount(1, 1, 7));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldUseTopNeighbour_WhenLeftIsUnavailable()
    {
        H264CavlcContextState state = new(4, 4);
        state.SetDecodedBlock(1, 0, 7, 9);

        Assert.Equal(9, state.GetPredictedNonZeroCount(1, 1, 7));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldAverageBothNeighboursWithRounding()
    {
        H264CavlcContextState state = new(4, 4);
        state.SetDecodedBlock(0, 1, 3, 4);
        state.SetDecodedBlock(1, 0, 3, 7);

        Assert.Equal(6, state.GetPredictedNonZeroCount(1, 1, 3));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldNotUseNeighbourFromAnotherSlice()
    {
        H264CavlcContextState state = new(4, 4);
        state.SetDecodedBlock(0, 1, 1, 12);
        state.SetDecodedBlock(1, 0, 2, 8);

        Assert.Equal(0, state.GetPredictedNonZeroCount(1, 1, 3));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldIgnoreNeighbourThatHasNotBeenDecoded()
    {
        H264CavlcContextState state = new(4, 4);
        state.SetDecodedBlock(0, 1, 4, 6);

        Assert.Equal(6, state.GetPredictedNonZeroCount(1, 1, 4));
    }

    [Fact]
    public void SetDecodedBlock_ShouldAllowZeroTotalCoefficientsAsAvailableNeighbour()
    {
        H264CavlcContextState state = new(2, 2);
        state.SetDecodedBlock(0, 1, 0, 0);
        state.SetDecodedBlock(1, 0, 0, 10);

        Assert.Equal(5, state.GetPredictedNonZeroCount(1, 1, 0));
    }

    [Fact]
    public void ReadBlock_ShouldDecodeAndRecordTotalCoefficientCount()
    {
        H264CavlcContextState state = new(2, 2);
        H264CavlcResidualBlockReader residualReader = new();
        H264BitReader bitReader = CreateReader("0101"); // one positive trailing-one coefficient

        var block = state.ReadBlock(residualReader, bitReader, 0, 0, 5);

        Assert.Equal(1, block.TotalCoefficients);
        Assert.Equal(1, state.GetPredictedNonZeroCount(1, 0, 5));
    }

    [Fact]
    public void ReadBlock_ShouldRejectChromaDcBlockSize()
    {
        H264CavlcContextState state = new(2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.ReadBlock(new H264CavlcResidualBlockReader(), CreateReader("1"), 0, 0, 0, 4));
    }

    [Fact]
    public void Constructor_ShouldRejectInvalidPictureDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new H264CavlcContextState(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new H264CavlcContextState(2, 0));
    }

    [Fact]
    public void SetDecodedBlock_ShouldRejectInvalidCoefficientCount()
    {
        H264CavlcContextState state = new(2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetDecodedBlock(0, 0, 0, 17));
    }

    [Fact]
    public void GetPredictedNonZeroCount_ShouldRejectOutOfRangeCoordinates()
    {
        H264CavlcContextState state = new(2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetPredictedNonZeroCount(2, 0, 0));
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