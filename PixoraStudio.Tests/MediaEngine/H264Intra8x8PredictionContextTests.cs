using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Intra8x8PredictionContextTests
{
    [Fact]
    public void GetPredictedMode_ShouldUseDcWhenBothNeighboursAreUnavailable()
    {
        var context = new H264Intra8x8PredictionContext();

        Assert.Equal(2u, context.GetPredictedMode(0, 0, 1));
        Assert.Equal(2u, context.GetPredictedMode(3, 3, 1));
    }

    [Fact]
    public void GetPredictedMode_ShouldUseMinimumWhenBothNeighboursAreAvailable()
    {
        var context = new H264Intra8x8PredictionContext();
        context.RecordMode(0, 1, 1, 7);
        context.RecordMode(1, 0, 1, 3);

        Assert.Equal(3u, context.GetPredictedMode(1, 1, 1));
    }

    [Theory]
    [InlineData(0u, 6u)]
    [InlineData(5u, 0u)]
    public void GetPredictedMode_ShouldUseDcWhenEitherNeighbourIsUnavailable(
        uint availableMode, uint unavailableSentinel)
    {
        var context = new H264Intra8x8PredictionContext();

        if (availableMode == 0)
            context.RecordMode(0, 1, 1, availableMode);
        else
            context.RecordMode(0, 1, 1, availableMode);

        Assert.Equal(2u, context.GetPredictedMode(1, 1, 1));
    }

    [Fact]
    public void GetPredictedMode_ShouldTreatDifferentSliceNeighboursAsUnavailable()
    {
        var context = new H264Intra8x8PredictionContext();
        context.RecordMode(0, 1, 10, 0);
        context.RecordMode(1, 0, 11, 1);

        Assert.Equal(2u, context.GetPredictedMode(1, 1, 10));
        Assert.Equal(2u, context.GetPredictedMode(1, 1, 11));
        Assert.Equal(2u, context.GetPredictedMode(1, 1, 12));
    }

    [Fact]
    public void ResolveAndRecord_ShouldResolvePreviousModeAndStoreIt()
    {
        var context = new H264Intra8x8PredictionContext();
        context.RecordMode(0, 1, 1, 5);
        context.RecordMode(1, 0, 1, 3);

        uint resolved = context.ResolveAndRecord(1, 1, 1, true, null);
        context.RecordMode(2, 0, 1, 4);

        Assert.Equal(3u, resolved);
        Assert.Equal(3u, context.GetPredictedMode(2, 1, 1));
    }

    [Fact]
    public void ResolveAndRecord_ShouldApplyRemainderModeRemapping()
    {
        var context = new H264Intra8x8PredictionContext();
        context.RecordMode(0, 1, 1, 4);
        context.RecordMode(1, 0, 1, 6);

        uint resolved = context.ResolveAndRecord(1, 1, 1, false, 4);

        Assert.Equal(5u, resolved);
    }

    [Fact]
    public void Reset_ShouldClearAllRecordedModes()
    {
        var context = new H264Intra8x8PredictionContext();
        context.RecordMode(0, 1, 1, 7);
        context.RecordMode(1, 0, 1, 4);

        context.Reset();

        Assert.Equal(2u, context.GetPredictedMode(1, 1, 1));
    }

    [Fact]
    public void RecordMode_ShouldRejectInvalidArguments()
    {
        var context = new H264Intra8x8PredictionContext();

        Assert.Throws<ArgumentOutOfRangeException>(() => context.RecordMode(-1, 0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.RecordMode(0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.RecordMode(0, 0, 1, 9));
    }

    [Fact]
    public void GetPredictedMode_ShouldRejectNegativeCoordinates()
    {
        var context = new H264Intra8x8PredictionContext();

        Assert.Throws<ArgumentOutOfRangeException>(() => context.GetPredictedMode(0, -1, 1));
    }
}
