using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264MotionVectorPictureStateTests
{
    private readonly H264MotionVectorPredictor _predictor = new();

    [Fact]
    public void GetCandidates_ShouldResolveLeftAboveAndAboveRightFromPictureGrid()
    {
        var state = new H264MotionVectorPictureState(3, 3);
        H264MotionVectorCandidate left = Candidate(0, 11, 12);
        H264MotionVectorCandidate above = Candidate(1, 21, 22);
        H264MotionVectorCandidate aboveRight = Candidate(2, 31, 32);

        state.RecordInterPartition(3, 5, 1, 1, 7, left.ReferenceIndex, left.MotionVector);
        state.RecordInterPartition(5, 3, 1, 1, 7, above.ReferenceIndex, above.MotionVector);
        state.RecordInterPartition(6, 3, 1, 1, 7, aboveRight.ReferenceIndex, aboveRight.MotionVector);

        H264MotionVectorNeighbourCandidates result = state.GetCandidates(4, 4, 2, 2, 7);

        Assert.Equal(left, result.Left);
        Assert.Equal(above, result.Above);
        Assert.Equal(aboveRight, result.AboveRight);
        Assert.Null(result.AboveLeft);
    }

    [Fact]
    public void GetCandidates_ShouldExposeAboveLeftForPredictorFallback()
    {
        var state = new H264MotionVectorPictureState(2, 2);
        H264MotionVectorCandidate aboveLeft = Candidate(3, 41, -9);
        // For this partition C lies beyond the right picture edge; D is at (5, 3).
        state.RecordInterPartition(5, 3, 1, 1, 1,
            aboveLeft.ReferenceIndex, aboveLeft.MotionVector);

        H264MotionVectorNeighbourCandidates result = state.GetCandidates(6, 4, 2, 2, 1);

        Assert.Null(result.AboveRight);
        Assert.Equal(aboveLeft, result.AboveLeft);

        H264MotionVector reconstructed = state.ReconstructAndRecord(
            _predictor, 6, 4, 2, 2, 1, 3,
            new H264MotionVectorDifference(1, 2));
        Assert.Equal(new H264MotionVector(42, -7), reconstructed);
    }

    [Fact]
    public void GetCandidates_ShouldTreatDifferentSliceAsUnavailable()
    {
        var state = new H264MotionVectorPictureState(2, 2);
        state.RecordInterPartition(3, 3, 1, 1, 10, 0, new H264MotionVector(7, 8));

        H264MotionVectorNeighbourCandidates sameSlice = state.GetCandidates(4, 3, 1, 1, 10);
        H264MotionVectorNeighbourCandidates otherSlice = state.GetCandidates(4, 3, 1, 1, 11);

        Assert.NotNull(sameSlice.Left);
        Assert.Null(otherSlice.Left);
    }

    [Fact]
    public void GetCandidates_ShouldTreatPictureEdgesAndUndecodedRegionsAsUnavailable()
    {
        var state = new H264MotionVectorPictureState(1, 1);

        H264MotionVectorNeighbourCandidates result = state.GetCandidates(0, 0, 4, 4, 0);

        Assert.Null(result.Left);
        Assert.Null(result.Above);
        Assert.Null(result.AboveRight);
        Assert.Null(result.AboveLeft);
    }

    [Fact]
    public void GetCandidates_ShouldReturnIntraMarkedRegionsAsUnavailable()
    {
        var state = new H264MotionVectorPictureState(2, 1);
        state.RecordUnavailableRegion(3, 0, 1, 1, 0);

        H264MotionVectorNeighbourCandidates result = state.GetCandidates(4, 0, 1, 1, 0);

        Assert.Null(result.Left);
    }

    [Fact]
    public void ReconstructAndRecord_ShouldAddMvdAndPublishVectorForFollowingPartition()
    {
        var state = new H264MotionVectorPictureState(2, 1);
        state.RecordInterPartition(3, 0, 1, 1, 0, 2, new H264MotionVector(10, -4));

        H264MotionVector reconstructed = state.ReconstructAndRecord(
            _predictor, 4, 0, 1, 1, 0, 2,
            new H264MotionVectorDifference(3, 7));

        Assert.Equal(new H264MotionVector(13, 3), reconstructed);

        H264MotionVectorNeighbourCandidates following = state.GetCandidates(5, 0, 1, 1, 0);
        Assert.Equal(new H264MotionVectorCandidate(2, reconstructed), following.Left);
    }

    [Fact]
    public void ReconstructAndRecord_ShouldUsePredictorAndMvdForPartitionWithThreeNeighbours()
    {
        var state = new H264MotionVectorPictureState(3, 3);
        state.RecordInterPartition(3, 5, 1, 1, 0, 0, new H264MotionVector(10, 90));
        state.RecordInterPartition(5, 3, 1, 1, 0, 1, new H264MotionVector(30, 20));
        state.RecordInterPartition(6, 3, 1, 1, 0, 2, new H264MotionVector(20, 50));

        H264MotionVector result = state.ReconstructAndRecord(
            _predictor, 4, 4, 2, 2, 0, 3,
            new H264MotionVectorDifference(-2, 5));

        Assert.Equal(new H264MotionVector(18, 55), result);
    }

    [Fact]
    public void ReconstructAndRecord_ShouldRejectIntegerOverflow()
    {
        var state = new H264MotionVectorPictureState(1, 1);
        state.RecordInterPartition(0, 0, 1, 1, 0, 0,
            new H264MotionVector(int.MaxValue, 0));

        Assert.Throws<OverflowException>(() => state.ReconstructAndRecord(
            _predictor, 1, 0, 1, 1, 0, 0,
            new H264MotionVectorDifference(int.MaxValue, 0)));
    }

    [Fact]
    public void RecordInterPartition_ShouldRejectOverlappingRegions()
    {
        var state = new H264MotionVectorPictureState(1, 1);
        state.RecordInterPartition(0, 0, 2, 2, 0, 0, new H264MotionVector(0, 0));

        Assert.Throws<InvalidOperationException>(() =>
            state.RecordInterPartition(1, 1, 1, 1, 0, 0, new H264MotionVector(1, 1)));
    }

    [Fact]
    public void RecordUnavailableRegion_ShouldPreventLaterOverlappingWrite()
    {
        var state = new H264MotionVectorPictureState(1, 1);
        state.RecordUnavailableRegion(0, 0, 1, 1, 0);

        Assert.Throws<InvalidOperationException>(() =>
            state.RecordInterPartition(0, 0, 1, 1, 0, 0, new H264MotionVector(0, 0)));
    }

    [Fact]
    public void ConstructorAndRegionMethods_ShouldValidateDimensionsAndCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new H264MotionVectorPictureState(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new H264MotionVectorPictureState(1, -1));

        var state = new H264MotionVectorPictureState(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.GetCandidates(-1, 0, 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.GetCandidates(0, 0, 0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.RecordInterPartition(0, 0, 1, 1, -1, 0, new H264MotionVector(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.RecordInterPartition(0, 0, 1, 1, 0, -1, new H264MotionVector(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.RecordInterPartition(7, 0, 2, 1, 0, 0, new H264MotionVector(0, 0)));
    }


    [Fact]
    public void ReconstructAndRecord_ShouldClipMotionVectorToNormativeRange()
    {
        var state = new H264MotionVectorPictureState(2, 2);
        state.RecordInterPartition(3, 4, 1, 1, 0, 0,
            new H264MotionVector(32760, -32760));

        H264MotionVector result = state.ReconstructAndRecord(
            _predictor, 4, 4, 1, 1, 0, 0,
            new H264MotionVectorDifference(20, -20));

        Assert.Equal(new H264MotionVector(32767, -32768), result);
    }

    private static H264MotionVectorCandidate Candidate(int reference, int x, int y) =>
        new(reference, new H264MotionVector(x, y));
}
