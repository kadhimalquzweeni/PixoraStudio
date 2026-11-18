using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264TemporalDirectMotionFieldTests
{
    [Fact]
    public void GetContext_ShouldPreferCollocatedList0Motion()
    {
        H264MotionVectorPictureState l0 = new(1, 1);
        H264MotionVectorPictureState l1 = new(1, 1);
        l0.RecordInterPartition(0, 0, 1, 1, 3, 1, new H264MotionVector(4, 5));
        l1.RecordInterPartition(0, 0, 1, 1, 3, 0, new H264MotionVector(40, 50));
        H264TemporalDirectMotionField field = new(
            l0, l1,
            [new H264ReferencePicture(0, -4), new H264ReferencePicture(1, -2)],
            [new H264ReferencePicture(2, 8)], 6);

        H264TemporalDirectPredictionContext context =
            field.GetContext(0, 0, 1, 1, 10)!;

        Assert.Equal(new H264MotionVector(4, 5), context.CollocatedMotionVector);
        Assert.Equal(10, context.CurrentPictureOrderCount);
        Assert.Equal(6, context.CollocatedPictureOrderCount);
        Assert.Equal(-2, context.CollocatedReferencePictureOrderCount);
        Assert.False(context.CollocatedReferenceIsLongTerm);
    }

    [Fact]
    public void GetContext_ShouldFallBackToCollocatedList1WhenList0IsUnavailable()
    {
        H264MotionVectorPictureState l0 = new(1, 1);
        H264MotionVectorPictureState l1 = new(1, 1);
        l1.RecordInterPartition(0, 0, 1, 1, 0, 0, new H264MotionVector(-7, 9));
        H264TemporalDirectMotionField field = new(
            l0, l1, [], [new H264ReferencePicture(2, 3, true, 0)], 8);

        H264TemporalDirectPredictionContext context =
            field.GetContext(0, 0, 1, 1, 12)!;

        Assert.Equal(new H264MotionVector(-7, 9), context.CollocatedMotionVector);
        Assert.Equal(3, context.CollocatedReferencePictureOrderCount);
        Assert.True(context.CollocatedReferenceIsLongTerm);
    }

    [Fact]
    public void GetContext_WhenCollocatedBlockHasNoInterMotion_ShouldReturnNull()
    {
        H264TemporalDirectMotionField field = new(
            new H264MotionVectorPictureState(1, 1),
            new H264MotionVectorPictureState(1, 1), [], [], 0);

        Assert.Null(field.GetContext(0, 0, 1, 1, 4));
    }

    [Fact]
    public void GetContext_ShouldRejectInvalidStoredReferenceIndex()
    {
        H264MotionVectorPictureState l0 = new(1, 1);
        l0.RecordInterPartition(0, 0, 1, 1, 0, 2, new H264MotionVector(1, 1));
        H264TemporalDirectMotionField field = new(
            l0, new H264MotionVectorPictureState(1, 1),
            [new H264ReferencePicture(0, 0)], [], 0);

        Assert.Throws<FormatException>(() => field.GetContext(0, 0, 1, 1, 1));
    }
}
