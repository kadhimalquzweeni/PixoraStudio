using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BSpatialDirectMotionVectorDeriverTests
{
    [Fact]
    public void Derive_ShouldUseSpatialNeighboursEvenWhenCollocatedMotionWouldSetColZeroFlag()
    {
        H264MotionVectorPictureState list0 = new(2, 1);
        H264MotionVectorPictureState list1 = new(2, 1);
        list0.RecordInterPartition(3, 1, 1, 1, 0, 0, new H264MotionVector(20, -12));
        list1.RecordInterPartition(3, 1, 1, 1, 0, 0, new H264MotionVector(-16, 24));

        H264BSpatialDirectMotionVectorDeriver deriver = new();
        IReadOnlyList<H264ReconstructedBInterSubPartition> result = deriver.Derive(
            list0, list1,
            macroblockX: 1, macroblockY: 0,
            partitionX: 0, partitionY: 0,
            partitionWidth: 2, partitionHeight: 2,
            macroblockPartitionIndex: 0, sliceId: 0,
            direct8x8InferenceFlag: true);

        H264ReconstructedBInterSubPartition direct = Assert.Single(result);
        Assert.Equal(0, direct.ReferenceIndexL0);
        Assert.Equal(0, direct.ReferenceIndexL1);
        Assert.Equal(new H264MotionVector(20, -12), direct.MotionVectorL0);
        Assert.Equal(new H264MotionVector(-16, 24), direct.MotionVectorL1);
    }

    [Fact]
    public void Derive_WhenBothReferenceIndicesAreUnavailable_ShouldUseZeroDirectPrediction()
    {
        H264MotionVectorPictureState list0 = new(2, 1);
        H264MotionVectorPictureState list1 = new(2, 1);

        H264BSpatialDirectMotionVectorDeriver deriver = new();
        H264ReconstructedBInterSubPartition direct = Assert.Single(deriver.Derive(
            list0, list1,
            macroblockX: 1, macroblockY: 0,
            partitionX: 0, partitionY: 0,
            partitionWidth: 2, partitionHeight: 2,
            macroblockPartitionIndex: 0, sliceId: 0,
            direct8x8InferenceFlag: true));

        Assert.Equal(0, direct.ReferenceIndexL0);
        Assert.Equal(0, direct.ReferenceIndexL1);
        Assert.Equal(new H264MotionVector(0, 0), direct.MotionVectorL0);
        Assert.Equal(new H264MotionVector(0, 0), direct.MotionVectorL1);
    }
}
