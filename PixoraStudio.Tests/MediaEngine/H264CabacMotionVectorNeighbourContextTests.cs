using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacMotionVectorNeighbourContextTests
{
    [Fact]
    public void GetReferenceIndexContextIncrement_ShouldWeightTopNeighbourTwice()
    {
        H264CabacMotionVectorNeighbourContext context = new();
        context.Record(0, 0, 1, 1, 0, false, 0, 0);
        context.Record(0, 1, 0, 1, 2, false, 0, 0);

        Assert.Equal(2, context.GetReferenceIndexContextIncrement(
            0, 1, 1, 1, isBSlice: false));
    }

    [Fact]
    public void GetReferenceIndexContextIncrement_ShouldIgnoreDirectBNeighbours()
    {
        H264CabacMotionVectorNeighbourContext context = new();
        context.Record(1, 0, 1, 1, 2, true, 0, 0);
        context.Record(1, 1, 0, 1, 1, false, 0, 0);

        Assert.Equal(2, context.GetReferenceIndexContextIncrement(
            1, 1, 1, 1, isBSlice: true));
    }

    [Theory]
    [InlineData(2, 0, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(20, 15, 2)]
    public void GetMotionVectorDifferenceContextIncrement_ShouldApplyCombinedMagnitudeThresholds(
        int leftMvd, int topMvd, int expected)
    {
        H264CabacMotionVectorNeighbourContext context = new();
        context.Record(0, 0, 1, 1, 0, false, leftMvd, 0);
        context.Record(0, 1, 0, 1, 0, false, topMvd, 0);

        Assert.Equal(expected, context.GetMotionVectorDifferenceContextIncrement(
            0, 1, 1, sliceId: 1));
        Assert.Equal(expected, context.GetMotionVectorDifferenceContextIncrement(
            0, 1, 1, component: 1, sliceId: 1));
    }



    [Fact]
    public void GetMotionVectorDifferenceContextIncrement_ShouldCombineBothComponentsFromBothNeighbours()
    {
        H264CabacMotionVectorNeighbourContext context = new();
        context.Record(0, 0, 1, 1, 0, false, 2, 1);
        context.Record(0, 1, 0, 1, 0, false, 0, 0);

        // The left neighbour contributes |2| + |1| = 3, so both
        // horizontal and vertical MVD bins use context increment one.
        Assert.Equal(1, context.GetMotionVectorDifferenceContextIncrement(
            0, 1, 1, sliceId: 1));
        Assert.Equal(1, context.GetMotionVectorDifferenceContextIncrement(
            0, 1, 1, component: 0, sliceId: 1));
        Assert.Equal(1, context.GetMotionVectorDifferenceContextIncrement(
            0, 1, 1, component: 1, sliceId: 1));
    }

    [Fact]
    public void GetMotionVectorDifferenceContextIncrement_ShouldCombineLeftAndTopMagnitudesAcrossComponents()
    {
        H264CabacMotionVectorNeighbourContext context = new();
        context.Record(0, 0, 1, 1, 0, false, 3, 0);
        context.Record(0, 1, 0, 1, 0, false, 0, 30);

        // 3 + 30 = 33, selecting context increment two for either component.
        Assert.Equal(2, context.GetMotionVectorDifferenceContextIncrement(
            0, 1, 1, component: 0, sliceId: 1));
        Assert.Equal(2, context.GetMotionVectorDifferenceContextIncrement(
            0, 1, 1, component: 1, sliceId: 1));
    }

    [Fact]
    public void GetMotionVectorDifferenceContextIncrement_ShouldIgnoreOtherSlices()
    {
        H264CabacMotionVectorNeighbourContext context = new();
        context.Record(0, 0, 1, 2, 0, false, 40, 40);
        context.Record(0, 1, 0, 2, 0, false, 40, 40);

        Assert.Equal(0, context.GetMotionVectorDifferenceContextIncrement(
            0, 1, 1, component: 0, sliceId: 1));
    }

    [Fact]
    public void Reset_ShouldClearReferenceAndMvdState()
    {
        H264CabacMotionVectorNeighbourContext context = new();
        context.Record(0, 0, 0, 1, 2, false, 40, 40);

        context.Reset();

        Assert.Equal(0, context.GetReferenceIndexContextIncrement(
            0, 1, 0, 1, isBSlice: false));
        Assert.Equal(0, context.GetMotionVectorDifferenceContextIncrement(
            0, 1, 0, 1));
    }
}
