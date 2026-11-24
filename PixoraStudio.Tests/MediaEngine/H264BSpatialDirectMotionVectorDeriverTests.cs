using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BSpatialDirectMotionVectorDeriverTests
{
    private readonly H264BSpatialDirectMotionVectorDeriver _deriver = new();

    [Fact]
    public void Derive_ShouldUseTopLeftFourByFourBlockNeighboursForA_B_And_C()
    {
        H264MotionVectorPictureState list0 = new(3, 3);
        H264MotionVectorPictureState list1 = new(3, 3);

        // A=(3,4), B=(4,3), C=(8,3) for spatial-direct macroblock
        // of macroblock (1,1). The selected reference is B for each list.
        Record(list0, 3, 4, 2, 10, 20);
        Record(list0, 4, 3, 1, 30, 40);
        Record(list0, 8, 3, 3, 50, 60);
        Record(list1, 3, 4, 4, 11, 21);
        Record(list1, 4, 3, 2, 70, 80);
        Record(list1, 8, 3, 3, 51, 61);

        IReadOnlyList<H264ReconstructedBInterSubPartition> result = _deriver.Derive(
            list0, list1,
            macroblockX: 1, macroblockY: 1,
            partitionX: 0, partitionY: 0,
            partitionWidth: 2, partitionHeight: 2,
            macroblockPartitionIndex: 0, sliceId: 0,
            direct8x8InferenceFlag: true);

        H264ReconstructedBInterSubPartition partition = Assert.Single(result);
        Assert.Equal(1, partition.ReferenceIndexL0);
        Assert.Equal(2, partition.ReferenceIndexL1);
        Assert.Equal(new H264MotionVector(30, 40), partition.MotionVectorL0);
        Assert.Equal(new H264MotionVector(70, 80), partition.MotionVectorL1);

        Assert.True(list0.TryGetStoredMotionVector(4, 4, out H264MotionVectorCandidate stored0));
        Assert.Equal(new H264MotionVectorCandidate(1, new H264MotionVector(30, 40)), stored0);
        Assert.True(list1.TryGetStoredMotionVector(4, 4, out H264MotionVectorCandidate stored1));
        Assert.Equal(new H264MotionVectorCandidate(2, new H264MotionVector(70, 80)), stored1);
    }

    [Fact]
    public void Derive_ShouldUseAboveLeftWhenAboveRightIsUnavailable()
    {
        H264MotionVectorPictureState list0 = new(3, 3);
        H264MotionVectorPictureState list1 = new(3, 3);

        Record(list0, 3, 4, 4, 10, 10); // A
        Record(list0, 4, 3, 5, 20, 20); // B
        Record(list0, 3, 3, 1, 30, 40); // D; C=(8,3) is unavailable

        IReadOnlyList<H264ReconstructedBInterSubPartition> result = _deriver.Derive(
            list0, list1,
            macroblockX: 1, macroblockY: 1,
            partitionX: 0, partitionY: 0,
            partitionWidth: 2, partitionHeight: 2,
            macroblockPartitionIndex: 0, sliceId: 0,
            direct8x8InferenceFlag: true);

        H264ReconstructedBInterSubPartition partition = Assert.Single(result);
        Assert.Equal(1, partition.ReferenceIndexL0);
        Assert.Equal(new H264MotionVector(30, 40), partition.MotionVectorL0);
        Assert.Null(partition.ReferenceIndexL1);
        Assert.Null(partition.MotionVectorL1);
    }

    [Fact]
    public void Derive_WhenBothReferenceListsHaveNoCandidates_ShouldUseZeroReferenceAndMotion()
    {
        H264MotionVectorPictureState list0 = new(2, 2);
        H264MotionVectorPictureState list1 = new(2, 2);

        IReadOnlyList<H264ReconstructedBInterSubPartition> result = _deriver.Derive(
            list0, list1,
            macroblockX: 0, macroblockY: 0,
            partitionX: 0, partitionY: 0,
            partitionWidth: 2, partitionHeight: 2,
            macroblockPartitionIndex: 0, sliceId: 0,
            direct8x8InferenceFlag: true);

        H264ReconstructedBInterSubPartition partition = Assert.Single(result);
        Assert.Equal(0, partition.ReferenceIndexL0);
        Assert.Equal(0, partition.ReferenceIndexL1);
        Assert.Equal(new H264MotionVector(0, 0), partition.MotionVectorL0);
        Assert.Equal(new H264MotionVector(0, 0), partition.MotionVectorL1);
    }

    [Fact]
    public void Derive_WhenOnlyOneListHasCandidates_ShouldLeaveOtherListUnused()
    {
        H264MotionVectorPictureState list0 = new(3, 3);
        H264MotionVectorPictureState list1 = new(3, 3);
        Record(list0, 3, 4, 0, 12, -6);

        IReadOnlyList<H264ReconstructedBInterSubPartition> result = _deriver.Derive(
            list0, list1,
            macroblockX: 1, macroblockY: 1,
            partitionX: 0, partitionY: 0,
            partitionWidth: 2, partitionHeight: 2,
            macroblockPartitionIndex: 0, sliceId: 0,
            direct8x8InferenceFlag: true);

        H264ReconstructedBInterSubPartition partition = Assert.Single(result);
        Assert.Equal(0, partition.ReferenceIndexL0);
        Assert.Equal(new H264MotionVector(12, -6), partition.MotionVectorL0);
        Assert.Null(partition.ReferenceIndexL1);
        Assert.Null(partition.MotionVectorL1);
    }

    [Fact]
    public void Derive_ShouldApplyColZeroFlagToEachListUsingReferenceIndexZero()
    {
        H264MotionVectorPictureState list0 = new(3, 3);
        H264MotionVectorPictureState list1 = new(3, 3);
        Record(list0, 3, 4, 0, 12, 8);
        Record(list1, 3, 4, 0, -9, 5);

        IReadOnlyList<H264ReconstructedBInterSubPartition> result = _deriver.Derive(
            list0, list1,
            macroblockX: 1, macroblockY: 1,
            partitionX: 0, partitionY: 0,
            partitionWidth: 2, partitionHeight: 2,
            macroblockPartitionIndex: 0, sliceId: 0,
            direct8x8InferenceFlag: true,
            colZeroFlagProvider: static (_, _, _, _) => true);

        H264ReconstructedBInterSubPartition partition = Assert.Single(result);
        Assert.Equal(new H264MotionVector(0, 0), partition.MotionVectorL0);
        Assert.Equal(new H264MotionVector(0, 0), partition.MotionVectorL1);
    }

    private static void Record(
        H264MotionVectorPictureState state,
        int x,
        int y,
        int referenceIndex,
        int horizontal,
        int vertical) =>
        state.RecordInterPartition(
            x, y, 1, 1, sliceId: 0, referenceIndex: referenceIndex,
            motionVector: new H264MotionVector(horizontal, vertical));
}
