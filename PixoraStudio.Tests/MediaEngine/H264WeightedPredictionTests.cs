using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264WeightedPredictionTests
{
    [Fact]
    public void ApplyList0_WithNoWeightTable_ShouldReturnIndependentCopy()
    {
        H264MotionCompensatedBlock source = CreateBlock();
        H264MotionCompensatedBlock result =
            H264WeightedPrediction.ApplyList0(source, null, 0, 1);

        Assert.Equal(source.Luma, result.Luma);
        Assert.Equal(source.Cb, result.Cb);
        Assert.Equal(source.Cr, result.Cr);
        Assert.NotSame(source.Luma, result.Luma);
        Assert.NotSame(source.Cb, result.Cb);
        Assert.NotSame(source.Cr, result.Cr);
    }

    [Fact]
    public void ApplyList0_ShouldApplyExplicitLumaAndChromaWeightsAndOffsets()
    {
        H264MotionCompensatedBlock source = CreateBlock(100, 80, 60);
        H264PredictionWeight weight = new(
            true, 2, 5, true, [4, 6], [-3, 7]);
        H264PredictionWeightTable table = new(1, 2, [weight], []);

        H264MotionCompensatedBlock result =
            H264WeightedPrediction.ApplyList0(source, table, 0, 1);

        Assert.All(result.Luma, sample => Assert.Equal((byte)105, sample));
        Assert.All(result.Cb, sample => Assert.Equal((byte)77, sample));
        Assert.All(result.Cr, sample => Assert.Equal((byte)97, sample));
        Assert.All(source.Luma, sample => Assert.Equal((byte)100, sample));
    }

    [Fact]
    public void ApplyList0_WithAbsentFlags_ShouldUseDenominatorDefaultWeights()
    {
        H264MotionCompensatedBlock source = CreateBlock(101, 81, 61);
        H264PredictionWeight weight = new(
            false, null, null, false, [], []);
        H264PredictionWeightTable table = new(3, 2, [weight], []);

        H264MotionCompensatedBlock result =
            H264WeightedPrediction.ApplyList0(source, table, 0, 2);

        Assert.Equal(source.Luma, result.Luma);
        Assert.Equal(source.Cb, result.Cb);
        Assert.Equal(source.Cr, result.Cr);
    }

    [Fact]
    public void ApplyList0_ShouldClipWeightedSamplesToByteRange()
    {
        H264MotionCompensatedBlock source = CreateBlock(255, 0, 255);
        H264PredictionWeight weight = new(
            true, 127, 127, true, [-128, 127], [-128, 127]);
        H264PredictionWeightTable table = new(0, 0, [weight], []);

        H264MotionCompensatedBlock result =
            H264WeightedPrediction.ApplyList0(source, table, 0, 1);

        Assert.All(result.Luma, sample => Assert.Equal(byte.MaxValue, sample));
        Assert.All(result.Cb, sample => Assert.Equal((byte)0, sample));
        Assert.All(result.Cr, sample => Assert.Equal(byte.MaxValue, sample));
    }

    [Fact]
    public void ApplyList0_ShouldSelectWeightEntryByReferenceIndex()
    {
        H264PredictionWeight first = new(true, 1, 0, false, [], []);
        H264PredictionWeight second = new(true, 2, 0, false, [], []);
        H264PredictionWeightTable table = new(0, 0, [first, second], []);

        H264MotionCompensatedBlock result =
            H264WeightedPrediction.ApplyList0(CreateBlock(20), table, 1, 1);

        Assert.All(result.Luma, sample => Assert.Equal((byte)40, sample));
    }

    [Fact]
    public void ApplyList0_ShouldRejectInvalidReferenceIndexAndDenominators()
    {
        H264PredictionWeight weight = new(false, null, null, false, [], []);
        H264PredictionWeightTable table = new(8, 0, [weight], []);

        Assert.Throws<FormatException>(() =>
            H264WeightedPrediction.ApplyList0(CreateBlock(), table, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264WeightedPrediction.ApplyList0(CreateBlock(), table with { LumaLog2WeightDenom = 0 }, 1, 1));
    }

    [Fact]
    public void ApplyList0_ShouldRejectMalformedChromaWeightsAndUnsupportedFormat()
    {
        H264PredictionWeight malformed = new(
            false, null, null, true, [1], [0, 0]);
        H264PredictionWeightTable table = new(0, 0, [malformed], []);

        Assert.Throws<FormatException>(() =>
            H264WeightedPrediction.ApplyList0(CreateBlock(), table, 0, 1));
        Assert.Throws<NotSupportedException>(() =>
            H264WeightedPrediction.ApplyList0(CreateBlock(), null, 0, 3));
    }

    private static H264MotionCompensatedBlock CreateBlock(
        byte y = 100, byte cb = 80, byte cr = 60) =>
        new(4, 4, 2, 2,
            Enumerable.Repeat(y, 16).ToArray(),
            Enumerable.Repeat(cb, 4).ToArray(),
            Enumerable.Repeat(cr, 4).ToArray());
}
