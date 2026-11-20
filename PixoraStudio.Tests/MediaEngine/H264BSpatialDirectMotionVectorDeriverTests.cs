using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BSpatialDirectMotionVectorDeriverTests
{
    private readonly H264BSpatialDirectMotionVectorDeriver _deriver = new();

    [Fact]
    public void Derive_WhenList0HasNoCandidates_ShouldStillUseList0ReferenceZeroAndZeroVector()
    {
        H264MotionVectorPictureState list0 = new(1, 1);
        H264MotionVectorPictureState list1 = new(1, 1);
        list1.RecordInterPartition(1, 3, 1, 1, 0, 0, new H264MotionVector(12, 4));

        H264ReconstructedBInterSubPartition result = Assert.Single(Derive(list0, list1));

        Assert.Equal(0, result.ReferenceIndexL0);
        Assert.Equal(0, result.ReferenceIndexL1);
        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(12, 4), result.MotionVectorL1);
        Assert.True(list0.TryGetStoredMotionVector(2, 2, out H264MotionVectorCandidate storedL0));
        Assert.Equal(new H264MotionVector(0, 0), storedL0.MotionVector);
    }

    [Fact]
    public void Derive_WhenList1HasNoCandidates_ShouldStillUseList1ReferenceZeroAndZeroVector()
    {
        H264MotionVectorPictureState list0 = new(1, 1);
        H264MotionVectorPictureState list1 = new(1, 1);
        list0.RecordInterPartition(1, 3, 1, 1, 0, 0, new H264MotionVector(-8, 16));

        H264ReconstructedBInterSubPartition result = Assert.Single(Derive(list0, list1));

        Assert.Equal(0, result.ReferenceIndexL0);
        Assert.Equal(0, result.ReferenceIndexL1);
        Assert.Equal(new H264MotionVector(-8, 16), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL1);
        Assert.True(list1.TryGetStoredMotionVector(2, 2, out H264MotionVectorCandidate storedL1));
        Assert.Equal(new H264MotionVector(0, 0), storedL1.MotionVector);
    }

    [Fact]
    public void Derive_WhenBothListsHaveNoCandidates_ShouldUseReferenceZeroAndZeroVectorsForBoth()
    {
        H264MotionVectorPictureState list0 = new(1, 1);
        H264MotionVectorPictureState list1 = new(1, 1);

        H264ReconstructedBInterSubPartition result = Assert.Single(Derive(list0, list1));

        Assert.Equal(0, result.ReferenceIndexL0);
        Assert.Equal(0, result.ReferenceIndexL1);
        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL1);
    }

    [Fact]
    public void Derive_WhenColZeroFlagIsSet_ShouldZeroBothReferenceZeroVectors()
    {
        H264MotionVectorPictureState list0 = new(1, 1);
        H264MotionVectorPictureState list1 = new(1, 1);
        list1.RecordInterPartition(1, 3, 1, 1, 0, 0, new H264MotionVector(1, -1));

        H264ReconstructedBInterSubPartition result = Assert.Single(
            _deriver.Derive(
                list0, list1, 0, 0, 2, 2, 2, 2,
                0, 0, true, static (_, _, _, _) => true));

        Assert.Equal(0, result.ReferenceIndexL0);
        Assert.Equal(0, result.ReferenceIndexL1);
        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(0, 0), result.MotionVectorL1);
    }

    [Fact]
    public void Derive_WhenBothListsHaveCandidates_ShouldKeepIndependentSpatialPredictors()
    {
        H264MotionVectorPictureState list0 = new(1, 1);
        H264MotionVectorPictureState list1 = new(1, 1);
        list0.RecordInterPartition(1, 3, 1, 1, 0, 1, new H264MotionVector(12, 4));
        list1.RecordInterPartition(1, 3, 1, 1, 0, 2, new H264MotionVector(-6, 10));

        H264ReconstructedBInterSubPartition result = Assert.Single(Derive(list0, list1));

        Assert.Equal(1, result.ReferenceIndexL0);
        Assert.Equal(2, result.ReferenceIndexL1);
        Assert.Equal(new H264MotionVector(12, 4), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(-6, 10), result.MotionVectorL1);
    }

    private IReadOnlyList<H264ReconstructedBInterSubPartition> Derive(
        H264MotionVectorPictureState list0,
        H264MotionVectorPictureState list1) =>
        _deriver.Derive(
            list0, list1, 0, 0, 2, 2, 2, 2,
            0, 0, true);
}
