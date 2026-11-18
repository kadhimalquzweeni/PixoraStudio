using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Intra4x4PredictionContextTests
{
    [Fact]
    public void GetPredictedMode_ShouldUseDcModeWhenBothNeighboursAreUnavailable()
    {
        var context = new H264Intra4x4PredictionContext();

        Assert.Equal(2u, context.GetPredictedMode(0, 0, 1));
        Assert.Equal(2u, context.GetPredictedMode(3, 3, 1));
    }

    [Fact]
    public void GetPredictedMode_ShouldUseMinimumWhenBothNeighboursAreAvailable()
    {
        var context = new H264Intra4x4PredictionContext();
        context.RecordMode(0, 1, 1, 7);
        context.RecordMode(1, 0, 1, 3);

        Assert.Equal(3u, context.GetPredictedMode(1, 1, 1));
    }

    [Theory]
    [InlineData(0, 6, 2)]
    [InlineData(5, 0, 2)]
    [InlineData(4, 6, 4)]
    public void GetPredictedMode_ShouldApplyDcFallbackToUnavailableNeighbour(
        uint leftMode, uint topMode, uint expected)
    {
        var context = new H264Intra4x4PredictionContext();
        if (leftMode != 0)
            context.RecordMode(0, 1, 1, leftMode);
        if (topMode != 0)
            context.RecordMode(1, 0, 1, topMode);

        Assert.Equal(expected, context.GetPredictedMode(1, 1, 1));
    }

    [Fact]
    public void GetPredictedMode_ShouldTreatDifferentSliceNeighboursAsUnavailable()
    {
        var context = new H264Intra4x4PredictionContext();
        context.RecordMode(0, 1, 10, 0);
        context.RecordMode(1, 0, 11, 1);

        Assert.Equal(2u, context.GetPredictedMode(1, 1, 10));
        Assert.Equal(2u, context.GetPredictedMode(1, 1, 11));
        Assert.Equal(2u, context.GetPredictedMode(1, 1, 12));
    }

    [Fact]
    public void ResolveAndRecord_ShouldResolvePreviousModeAndStoreIt()
    {
        var context = new H264Intra4x4PredictionContext();
        context.RecordMode(0, 1, 1, 5);
        context.RecordMode(1, 0, 1, 3);

        uint resolved = context.ResolveAndRecord(1, 1, 1, true, null);

        Assert.Equal(3u, resolved);
        Assert.Equal(3u, context.GetPredictedMode(2, 1, 1));
    }

    [Fact]
    public void ResolveAndRecord_ShouldApplyRemainderModeRemapping()
    {
        var context = new H264Intra4x4PredictionContext();
        context.RecordMode(0, 1, 1, 4);
        context.RecordMode(1, 0, 1, 6);

        uint resolved = context.ResolveAndRecord(1, 1, 1, false, 4);

        Assert.Equal(5u, resolved);
    }

    [Fact]
    public void Reset_ShouldClearAllRecordedModes()
    {
        var context = new H264Intra4x4PredictionContext();
        context.RecordMode(0, 1, 1, 7);
        context.RecordMode(1, 0, 1, 4);

        context.Reset();

        Assert.Equal(2u, context.GetPredictedMode(1, 1, 1));
    }

    [Fact]
    public void RecordMode_ShouldRejectInvalidArguments()
    {
        var context = new H264Intra4x4PredictionContext();

        Assert.Throws<ArgumentOutOfRangeException>(() => context.RecordMode(-1, 0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.RecordMode(0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.RecordMode(0, 0, 1, 9));
    }

    [Fact]
    public void GetPredictedMode_ShouldRejectNegativeCoordinates()
    {
        var context = new H264Intra4x4PredictionContext();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            context.GetPredictedMode(-1, 0, 1));
    }
}
