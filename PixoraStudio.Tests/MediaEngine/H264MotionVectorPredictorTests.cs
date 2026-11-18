using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264MotionVectorPredictorTests
{
    private readonly H264MotionVectorPredictor _predictor = new();

    [Fact]
    public void Predict_ShouldReturnZeroWhenAllNeighboursUnavailable()
    {
        Assert.Equal(new H264MotionVector(0, 0),
            _predictor.Predict(null, null, null, null, 0));
    }

    [Fact]
    public void Predict_ShouldUseOnlyAvailableCandidate()
    {
        H264MotionVectorCandidate left = Candidate(2, 12, -7);

        Assert.Equal(left.MotionVector,
            _predictor.Predict(left, null, null, null, 2));
    }

    [Fact]
    public void Predict_ShouldPreferUniqueReferenceIndexMatch()
    {
        H264MotionVectorCandidate left = Candidate(0, 10, 10);
        H264MotionVectorCandidate above = Candidate(1, 30, 40);
        H264MotionVectorCandidate aboveRight = Candidate(2, -5, 8);

        Assert.Equal(aboveRight.MotionVector,
            _predictor.Predict(left, above, aboveRight, null, 2));
    }

    [Fact]
    public void Predict_ShouldUseComponentWiseMedianWhenNoUniqueReferenceMatch()
    {
        H264MotionVectorCandidate left = Candidate(0, 10, 90);
        H264MotionVectorCandidate above = Candidate(1, 30, 20);
        H264MotionVectorCandidate aboveRight = Candidate(2, 20, 50);

        Assert.Equal(new H264MotionVector(20, 50),
            _predictor.Predict(left, above, aboveRight, null, 3));
    }

    [Fact]
    public void Predict_ShouldUseZeroVectorForUnavailableCandidateInMedian()
    {
        H264MotionVectorCandidate left = Candidate(0, 12, -6);
        H264MotionVectorCandidate above = Candidate(0, 6, 12);

        Assert.Equal(new H264MotionVector(6, 0),
            _predictor.Predict(left, above, null, null, 1));
    }

    [Fact]
    public void Predict_ShouldFallbackToAboveLeftWhenAboveRightUnavailable()
    {
        H264MotionVectorCandidate left = Candidate(0, 1, 2);
        H264MotionVectorCandidate above = Candidate(0, 3, 4);
        H264MotionVectorCandidate aboveLeft = Candidate(1, 9, 10);

        Assert.Equal(aboveLeft.MotionVector,
            _predictor.Predict(left, above, null, aboveLeft, 1));
    }

    [Theory]
    [InlineData(0, 8, 9)] // top 16x8 uses B
    [InlineData(1, 4, 5)] // bottom 16x8 uses A
    public void Predict_SixteenByEight_ShouldUseNormativePreferredCandidate(
        int partitionIndex, int expectedX, int expectedY)
    {
        H264MotionVectorCandidate left = Candidate(0, 4, 5);
        H264MotionVectorCandidate above = Candidate(0, 8, 9);
        H264MotionVectorCandidate aboveRight = Candidate(1, 20, 21);

        Assert.Equal(new H264MotionVector(expectedX, expectedY),
            _predictor.Predict(left, above, aboveRight, null, 0,
                H264MotionVectorPartitionShape.SixteenByEight, partitionIndex));
    }

    [Theory]
    [InlineData(0, 4, 5)] // left 8x16 uses A
    [InlineData(1, 8, 9)] // right 8x16 uses C
    public void Predict_EightBySixteen_ShouldUseNormativePreferredCandidate(
        int partitionIndex, int expectedX, int expectedY)
    {
        H264MotionVectorCandidate left = Candidate(0, 4, 5);
        H264MotionVectorCandidate above = Candidate(1, 30, 31);
        H264MotionVectorCandidate aboveRight = Candidate(0, 8, 9);

        Assert.Equal(new H264MotionVector(expectedX, expectedY),
            _predictor.Predict(left, above, aboveRight, null, 0,
                H264MotionVectorPartitionShape.EightBySixteen, partitionIndex));
    }

    [Fact]
    public void Predict_ShouldFallBackToGeneralRuleWhenPreferredCandidateDoesNotMatchReference()
    {
        H264MotionVectorCandidate left = Candidate(1, 100, 100);
        H264MotionVectorCandidate above = Candidate(0, 8, 9);
        H264MotionVectorCandidate aboveRight = Candidate(1, 20, 21);

        Assert.Equal(above.MotionVector,
            _predictor.Predict(left, above, aboveRight, null, 0,
                H264MotionVectorPartitionShape.SixteenByEight, 0));
    }

    [Fact]
    public void Predict_ShouldValidateReferenceIndexAndPartitionIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _predictor.Predict(null, null, null, null, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _predictor.Predict(null, null, null, null, 0,
                H264MotionVectorPartitionShape.SixteenBySixteen, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _predictor.Predict(null, null, null, null, 0,
                H264MotionVectorPartitionShape.SubMacroblock, 4));
    }

    private static H264MotionVectorCandidate Candidate(int reference, int x, int y) =>
        new(reference, new H264MotionVector(x, y));
}
