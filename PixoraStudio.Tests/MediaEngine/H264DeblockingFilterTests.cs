using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DeblockingFilterTests
{
    [Fact]
    public void DeriveThresholds_ShouldAverageQpAndApplyIndependentOffsets()
    {
        H264DeblockingThresholds thresholds =
            H264DeblockingFilter.DeriveThresholds(30, 32, 1, -1, 2);

        Assert.Equal(33, thresholds.IndexA);
        Assert.Equal(29, thresholds.IndexB);
        Assert.Equal(36, thresholds.Alpha);
        Assert.Equal(7, thresholds.Beta);
        Assert.Equal(2, thresholds.Tc0);
    }

    [Fact]
    public void DeriveThresholds_ShouldClipIndicesToZeroAndFiftyOne()
    {
        H264DeblockingThresholds low =
            H264DeblockingFilter.DeriveThresholds(0, 0, -6, -6, 1);
        H264DeblockingThresholds high =
            H264DeblockingFilter.DeriveThresholds(51, 51, 6, 6, 3);

        Assert.Equal(0, low.IndexA);
        Assert.Equal(0, low.IndexB);
        Assert.Equal(51, high.IndexA);
        Assert.Equal(51, high.IndexB);
        Assert.Equal(25, high.Tc0);
    }

    [Fact]
    public void FilterEdge_WithBoundaryStrengthZero_ShouldLeaveSamplesUnchanged()
    {
        byte[] plane = CreateEdgePlane(100, 100, 100, 100, 102, 102, 102, 102);
        byte[] before = (byte[])plane.Clone();

        bool changed = H264DeblockingFilter.FilterEdge(
            plane, 6, 1, 12, 4,
            new H264DeblockingEdgeParameters(0, 40, 40, 0, 0, false));

        Assert.False(changed);
        Assert.Equal(before, plane);
    }

    [Fact]
    public void FilterEdge_WithWeakLumaBoundary_ShouldAdjustP0Q0AndEligibleP1Q1()
    {
        byte[] plane = CreateEdgePlane(100, 100, 100, 100, 102, 102, 102, 102);

        bool changed = H264DeblockingFilter.FilterEdge(
            plane, 6, 1, 12, 4,
            new H264DeblockingEdgeParameters(1, 40, 40, 0, 0, false));

        Assert.True(changed);
        Assert.Equal((byte)101, plane[5]);
        Assert.Equal((byte)101, plane[6]);
        Assert.Equal((byte)100, plane[4]);
        Assert.Equal((byte)101, plane[7]);
    }

    [Fact]
    public void FilterEdge_WithWeakChromaBoundary_ShouldOnlyAdjustP0Q0()
    {
        byte[] plane = CreateEdgePlane(100, 100, 100, 100, 102, 102, 102, 102);

        bool changed = H264DeblockingFilter.FilterEdge(
            plane, 6, 1, 12, 4,
            new H264DeblockingEdgeParameters(1, 40, 40, 0, 0, true));

        Assert.True(changed);
        Assert.Equal((byte)101, plane[5]);
        Assert.Equal((byte)101, plane[6]);
        Assert.Equal((byte)100, plane[4]);
        Assert.Equal((byte)102, plane[7]);
    }

    [Fact]
    public void FilterEdge_WithStrongLumaBoundary_ShouldApplyThreeTapFilteringWhenEligible()
    {
        byte[] plane = CreateEdgePlane(108, 109, 110, 110, 112, 112, 113, 114);

        bool changed = H264DeblockingFilter.FilterEdge(
            plane, 6, 1, 12, 4,
            new H264DeblockingEdgeParameters(4, 40, 40, 0, 0, false));

        Assert.True(changed);
        Assert.Equal((byte)111, plane[5]);
        Assert.Equal((byte)111, plane[6]);
        Assert.Equal((byte)110, plane[4]);
        Assert.Equal((byte)112, plane[7]);
        Assert.Equal((byte)109, plane[3]);
        Assert.Equal((byte)113, plane[8]);
    }

    [Fact]
    public void FilterEdge_WithStrongChromaBoundary_ShouldUseChromaStyleFiltering()
    {
        byte[] plane = CreateEdgePlane(90, 95, 100, 110, 114, 120, 125, 130);

        bool changed = H264DeblockingFilter.FilterEdge(
            plane, 6, 1, 12, 4,
            new H264DeblockingEdgeParameters(4, 40, 40, 0, 0, true));

        Assert.True(changed);
        Assert.Equal((byte)108, plane[5]);
        Assert.Equal((byte)114, plane[6]);
        Assert.Equal((byte)100, plane[4]);
        Assert.Equal((byte)120, plane[7]);
    }

    [Fact]
    public void FilterEdge_WhenAlphaOrBetaThresholdFails_ShouldNotModifySamples()
    {
        byte[] plane = CreateEdgePlane(20, 20, 20, 20, 200, 200, 200, 200);
        byte[] before = (byte[])plane.Clone();

        bool changed = H264DeblockingFilter.FilterEdge(
            plane, 6, 1, 12, 4,
            new H264DeblockingEdgeParameters(4, 10, 10, 0, 0, false));

        Assert.False(changed);
        Assert.Equal(before, plane);
    }

    [Fact]
    public void FilterEdge_ShouldRejectAnIncompleteEdgeNeighbourhood()
    {
        byte[] plane = new byte[12];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264DeblockingFilter.FilterEdge(
                plane, 2, 1, 3, 4,
                new H264DeblockingEdgeParameters(1, 30, 30, 0, 0, false)));
    }

    [Fact]
    public void FilterEdge_ShouldRejectInvalidBoundaryParameters()
    {
        byte[] plane = CreateEdgePlane(100, 100, 100, 100, 102, 102, 102, 102);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264DeblockingFilter.FilterEdge(
                plane, 6, 1, 12, 4,
                new H264DeblockingEdgeParameters(5, 40, 40, 0, 0, false)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264DeblockingFilter.FilterEdge(
                plane, 6, 1, 12, 4,
                new H264DeblockingEdgeParameters(1, 52, 40, 0, 0, false)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264DeblockingFilter.FilterEdge(
                plane, 6, 1, 12, 4,
                new H264DeblockingEdgeParameters(1, 40, 40, 7, 0, false)));
    }

    private static byte[] CreateEdgePlane(
        byte p3, byte p2, byte p1, byte p0,
        byte q0, byte q1, byte q2, byte q3)
    {
        byte[] plane = new byte[12 * 4];
        byte[] samples = [p3, p2, p1, p0, q0, q1, q2, q3];
        for (int y = 0; y < 4; y++)
            Array.Copy(samples, 0, plane, y * 12 + 2, samples.Length);
        return plane;
    }
}
