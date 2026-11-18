using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BTemporalDirectMotionVectorDeriverTests
{
    private readonly H264BTemporalDirectMotionVectorDeriver _deriver = new();

    [Fact]
    public void Derive_WhenCurrentAndCollocatedReferenceHaveEqualPoc_ShouldUseUnscaledVector()
    {
        H264TemporalDirectMotionVectorResult result = _deriver.Derive(
            new H264TemporalDirectPredictionContext(
                new H264MotionVector(12, -8), 10, 0, 0, false));

        Assert.Equal(new H264MotionVector(12, -8), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL1);
        Assert.Equal(256, result.DistanceScaleFactor);
    }

    [Fact]
    public void Derive_WhenCollocatedReferenceIsLongTerm_ShouldNotScale()
    {
        H264TemporalDirectMotionVectorResult result = _deriver.Derive(
            new H264TemporalDirectPredictionContext(
                new H264MotionVector(-17, 9), 10, 0, -2, true));

        Assert.Equal(new H264MotionVector(-17, 9), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL1);
        Assert.Equal(256, result.DistanceScaleFactor);
    }

    [Fact]
    public void Derive_WithEqualTemporalDistances_ShouldSplitCollocatedVectorEvenly()
    {
        H264TemporalDirectMotionVectorResult result = _deriver.Derive(
            new H264TemporalDirectPredictionContext(
                new H264MotionVector(16, -16), 5, 0, 10, false));

        Assert.Equal(128, result.DistanceScaleFactor);
        Assert.Equal(new H264MotionVector(8, -8), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(-8, 8), result.MotionVectorL1);
    }

    [Fact]
    public void Derive_WhenColZeroFlagApplies_ShouldZeroBothPredictionLists()
    {
        H264TemporalDirectMotionVectorResult result = _deriver.Derive(
            new H264TemporalDirectPredictionContext(
                new H264MotionVector(1, -1), 10, 0, -10, false,
                CollocatedReferenceIndex: 0, CollocatedPictureIsShortTerm: true));

        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL1);
    }

    [Fact]
    public void Derive_WhenCollocatedPictureIsLongTerm_ShouldNotApplyColZeroFlag()
    {
        H264TemporalDirectMotionVectorResult result = _deriver.Derive(
            new H264TemporalDirectPredictionContext(
                new H264MotionVector(1, -1), 10, 0, -10, false,
                CollocatedReferenceIndex: 0, CollocatedPictureIsShortTerm: false));

        Assert.NotEqual(new H264MotionVector(0, 0), result.MotionVectorL0);
    }

    [Fact]
    public void Derive_WhenCollocatedReferenceIndexIsNotZero_ShouldNotApplyColZeroFlag()
    {
        H264TemporalDirectMotionVectorResult result = _deriver.Derive(
            new H264TemporalDirectPredictionContext(
                new H264MotionVector(1, -1), 10, 0, -10, false,
                CollocatedReferenceIndex: 1, CollocatedPictureIsShortTerm: true));

        Assert.NotEqual(new H264MotionVector(0, 0), result.MotionVectorL0);
    }

    [Fact]
    public void Derive_WithFutureCollocatedReference_ShouldProduceNegativeScale()
    {
        H264TemporalDirectMotionVectorResult result = _deriver.Derive(
            new H264TemporalDirectPredictionContext(
                new H264MotionVector(20, 4), 0, 10, 20, false));

        Assert.True(result.DistanceScaleFactor < 0);
        Assert.Equal(
            new H264MotionVector(
                (result.DistanceScaleFactor * 20 + 128) >> 8,
                (result.DistanceScaleFactor * 4 + 128) >> 8),
            result.MotionVectorL0);
        Assert.Equal(
            new H264MotionVector(
                result.MotionVectorL0.Horizontal - 20,
                result.MotionVectorL0.Vertical - 4),
            result.MotionVectorL1);
    }

    [Fact]
    public void Derive_ShouldClipCollocatedAndDerivedMotionVectors()
    {
        H264TemporalDirectMotionVectorResult result = _deriver.Derive(
            new H264TemporalDirectPredictionContext(
                new H264MotionVector(int.MaxValue, int.MinValue), 10000, -10000, 10000, false));

        Assert.InRange(result.DistanceScaleFactor, -1024, 1023);
        Assert.InRange(result.MotionVectorL0.Horizontal, -32768, 32767);
        Assert.InRange(result.MotionVectorL0.Vertical, -32768, 32767);
        Assert.InRange(result.MotionVectorL1.Horizontal, -32768, 32767);
        Assert.InRange(result.MotionVectorL1.Vertical, -32768, 32767);
    }
}
