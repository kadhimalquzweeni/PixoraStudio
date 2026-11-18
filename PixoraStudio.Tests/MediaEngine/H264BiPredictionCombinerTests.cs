using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BiPredictionCombinerTests
{
    [Fact]
    public void CombineDefault_ShouldAverageEachPlaneWithRounding()
    {
        H264MotionCompensatedBlock p0 = Block(10, 20, 30);
        H264MotionCompensatedBlock p1 = Block(11, 21, 31);

        H264MotionCompensatedBlock result =
            H264BiPredictionCombiner.CombineDefault(p0, p1, 1);

        Assert.All(result.Luma, value => Assert.Equal((byte)11, value));
        Assert.All(result.Cb, value => Assert.Equal((byte)21, value));
        Assert.All(result.Cr, value => Assert.Equal((byte)31, value));
    }

    [Fact]
    public void CombineExplicit_ShouldApplyPerListWeightsOffsetsAndDenominator()
    {
        H264PredictionWeight weight0 = new(true, 2, 3, true, [4, 2], [1, -1]);
        H264PredictionWeight weight1 = new(true, 2, 5, true, [4, 6], [3, 5]);
        H264PredictionWeightTable table = new(1, 2, [weight0], [weight1]);

        H264MotionCompensatedBlock result = H264BiPredictionCombiner.CombineExplicit(
            Block(100, 80, 60), Block(50, 40, 20), table, 0, 0, 1);

        // Luma: ((2*100 + 2*50 + 2) >> 2) + ((3+5+1)>>1) = 77
        Assert.All(result.Luma, value => Assert.Equal((byte)77, value));
        // Cb: ((4*80 + 4*40 + 4) >> 3) + ((1+3+1)>>1) = 63
        Assert.All(result.Cb, value => Assert.Equal((byte)63, value));
        // Cr: ((2*60 + 6*20 + 4) >> 3) + ((-1+5+1)>>1) = 31
        Assert.All(result.Cr, value => Assert.Equal((byte)31, value));
    }

    [Fact]
    public void CombineImplicit_WithEqualPocOrLongTermReference_ShouldUseEqualWeights()
    {
        H264MotionCompensatedBlock p0 = Block(10, 20, 30);
        H264MotionCompensatedBlock p1 = Block(20, 40, 60);

        H264MotionCompensatedBlock equalPoc = H264BiPredictionCombiner.CombineImplicit(
            p0, p1, 5, 5, 5, false, false, 1);
        H264MotionCompensatedBlock longTerm = H264BiPredictionCombiner.CombineImplicit(
            p0, p1, 8, 2, 12, true, false, 1);

        Assert.All(equalPoc.Luma, value => Assert.Equal((byte)15, value));
        Assert.All(longTerm.Cb, value => Assert.Equal((byte)30, value));
    }

    [Fact]
    public void CombineImplicit_ShouldApplyPocDistanceWeights()
    {
        H264MotionCompensatedBlock p0 = Block(10, 10, 10);
        H264MotionCompensatedBlock p1 = Block(30, 30, 30);

        H264MotionCompensatedBlock result = H264BiPredictionCombiner.CombineImplicit(
            p0, p1, 5, 0, 10, false, false, 1);

        Assert.All(result.Luma, value => Assert.Equal((byte)20, value));
    }

    [Fact]
    public void Combine_ShouldRejectMismatchedPlanesAndInvalidWeightTables()
    {
        Assert.Throws<ArgumentException>(() =>
            H264BiPredictionCombiner.CombineDefault(Block(1, 2, 3), Block(1, 2, 3, 4, 2), 1));
        Assert.Throws<NotSupportedException>(() =>
            H264BiPredictionCombiner.CombineDefault(Block(1, 2, 3), Block(1, 2, 3), 3));

        H264PredictionWeight malformed = new(true, 1, 0, true, [1], [0, 0]);
        H264PredictionWeightTable table = new(0, 0, [malformed], [malformed]);
        Assert.Throws<FormatException>(() =>
            H264BiPredictionCombiner.CombineExplicit(
                Block(1, 2, 3), Block(1, 2, 3), table, 0, 0, 1));
    }

    private static H264MotionCompensatedBlock Block(
        byte y, byte cb, byte cr, int chromaWidth = 2, int chromaHeight = 2) =>
        new(4, 4, chromaWidth, chromaHeight,
            Enumerable.Repeat(y, 16).ToArray(),
            Enumerable.Repeat(cb, chromaWidth * chromaHeight).ToArray(),
            Enumerable.Repeat(cr, chromaWidth * chromaHeight).ToArray());
}
