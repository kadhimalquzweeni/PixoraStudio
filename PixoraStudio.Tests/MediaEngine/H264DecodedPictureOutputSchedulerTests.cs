using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DecodedPictureOutputSchedulerTests
{
    [Fact]
    public void Enqueue_ShouldApplyCompleteDeblockingMetadataBeforeOutput()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(0);
        H264DecodedPictureOutputScheduler scheduler = new();
        H264DecodedPictureSurface surface = Surface(sps, 1);
        H264DecodedPictureOutput output = Assert.Single(scheduler.Enqueue(
            surface, sps, Slice(0, 0, idr: true), 3));

        Assert.True(surface.IsDeblockingFilterApplied);
        Assert.Same(surface, output.Surface);
    }

    [Fact]
    public void CalculatePocBeforeReconstruction_ShouldEnqueueTheSamePrecomputedPoc()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(0);
        H264DecodedPictureOutputScheduler scheduler = new();
        H264SliceHeader slice = Slice(0, 0, idr: true);

        H264PictureOrderCount poc = scheduler.CalculatePictureOrderCount(sps, slice, 3);
        H264DecodedPictureOutput output = Assert.Single(scheduler.Enqueue(
            Surface(sps, 55), sps, slice, 3, poc));

        Assert.Equal(0, poc.PictureOrderCount);
        Assert.Equal(0, output.PictureOrderCount);
        Assert.Equal((byte)55, output.Surface.CopyLumaPlane()[0]);
    }

    [Fact]
    public void Enqueue_ShouldDerivePocAndUseVuiReorderLimit()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(1);
        H264DecodedPictureOutputScheduler scheduler = new();
        H264DecodedPictureSurface first = Surface(sps, 10);
        H264DecodedPictureSurface second = Surface(sps, 20);

        IReadOnlyList<H264DecodedPictureOutput> firstReady =
            scheduler.Enqueue(first, sps, Slice(0, 0), 1);
        IReadOnlyList<H264DecodedPictureOutput> secondReady =
            scheduler.Enqueue(second, sps, Slice(1, 2), 1);

        Assert.Empty(firstReady);
        H264DecodedPictureOutput output = Assert.Single(secondReady);
        Assert.Same(first, output.Surface);
        Assert.Equal(0, output.PictureOrderCount);
        Assert.Equal(1, scheduler.PendingCount);
        H264DecodedPictureOutput flushed = Assert.Single(scheduler.Flush());
        Assert.Same(second, flushed.Surface);
        Assert.Equal(2, flushed.PictureOrderCount);
    }

    [Fact]
    public void Enqueue_InvalidIdr_ShouldNotFlushPendingOutput()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(2);
        H264DecodedPictureOutputScheduler scheduler = new();
        H264DecodedPictureSurface prior = Surface(sps, 10);
        scheduler.Enqueue(prior, sps, Slice(0, 0), 1);

        Assert.Throws<FormatException>(() => scheduler.Enqueue(
            Surface(sps, 30), sps, Slice(0, 0, idr: true) with
            {
                PicOrderCntLsb = null
            }, 3));

        Assert.Equal(1, scheduler.PendingCount);
        Assert.Same(prior, Assert.Single(scheduler.Flush()).Surface);
    }

    [Fact]
    public void Enqueue_MmcoFive_ShouldBumpAllPriorPicturesBeforeNewPocEpoch()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(2);
        H264DecodedPictureOutputScheduler scheduler = new();
        H264DecodedPictureSurface idr = Surface(sps, 10);
        H264DecodedPictureSurface prior = Surface(sps, 15);
        scheduler.Enqueue(idr, sps, Slice(0, 0, idr: true), 3);
        scheduler.Enqueue(prior, sps, Slice(1, 6), 3);

        H264DecodedReferenceMarking marking = new(
            false, false, true,
            [new H264MemoryManagementControlOperation(5, null, null, null, null)]);
        H264SliceHeader mmco5 = Slice(2, 10) with
        {
            DeltaPicOrderCntBottom = -2,
            DecodedReferenceMarking = marking
        };

        IReadOnlyList<H264DecodedPictureOutput> priorOutputs = scheduler.Enqueue(
            Surface(sps, 20), sps, mmco5, 3);

        Assert.Equal(2, priorOutputs.Count);
        Assert.Same(idr, priorOutputs[0].Surface);
        Assert.Equal(0, priorOutputs[0].PictureOrderCount);
        Assert.Same(prior, priorOutputs[1].Surface);
        Assert.Equal(6, priorOutputs[1].PictureOrderCount);
        Assert.Equal(1, scheduler.PendingCount);
        H264DecodedPictureOutput current = Assert.Single(scheduler.Flush());
        Assert.Equal(0, current.PictureOrderCount);
    }

    [Fact]
    public void Enqueue_IdrWithNoOutputFlag_ShouldDiscardPriorPendingPictures()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(2);
        H264DecodedPictureOutputScheduler scheduler = new();
        H264DecodedPictureSurface prior = Surface(sps, 10);
        scheduler.Enqueue(prior, sps, Slice(0, 0), 1);

        H264DecodedPictureOutput? output = scheduler.Enqueue(
            Surface(sps, 30), sps,
            Slice(0, 0, idr: true, noOutputPrior: true), 3).FirstOrDefault();

        Assert.Null(output);
        Assert.Equal(1, scheduler.PendingCount);
        Assert.Equal(0, Assert.Single(scheduler.Flush()).PictureOrderCount);
    }

    [Fact]
    public void Enqueue_IdrWithoutNoOutputFlag_ShouldFlushPriorPicturesBeforeNewIdr()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(2);
        H264DecodedPictureOutputScheduler scheduler = new();
        H264DecodedPictureSurface prior = Surface(sps, 10);
        scheduler.Enqueue(prior, sps, Slice(0, 0), 1);

        IReadOnlyList<H264DecodedPictureOutput> outputs = scheduler.Enqueue(
            Surface(sps, 30), sps,
            Slice(0, 0, idr: true, noOutputPrior: false), 3);

        H264DecodedPictureOutput output = Assert.Single(outputs);
        Assert.Same(prior, output.Surface);
        Assert.Equal(0, output.PictureOrderCount);
        Assert.Equal(1, scheduler.PendingCount);
    }

    [Fact]
    public void Reset_ShouldClearPendingOutputAndPocHistory()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(2);
        H264DecodedPictureOutputScheduler scheduler = new();
        scheduler.Enqueue(Surface(sps, 10), sps, Slice(0, 0), 1);
        Assert.Equal(1, scheduler.PendingCount);

        scheduler.Reset();

        Assert.Equal(0, scheduler.PendingCount);
        scheduler.Enqueue(Surface(sps, 20), sps, Slice(0, 0), 1);
        H264DecodedPictureOutput output = Assert.Single(scheduler.Flush());
        Assert.Equal(0, output.PictureOrderCount);
    }

    [Fact]
    public void Enqueue_ShouldRejectReorderLimitGreaterThanMaxDecFrameBuffering()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(2, maxDecFrameBuffering: 1);

        Assert.Throws<FormatException>(() =>
            new H264DecodedPictureOutputScheduler().Enqueue(
                Surface(sps, 1), sps, Slice(0, 0), 1));
    }

    [Fact]
    public void Enqueue_ShouldRejectMissingVuiWhenSpsSignalsVuiPresence()
    {
        H264SequenceParameterSet sps = new(
            0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, true);

        Assert.Throws<FormatException>(() =>
            new H264DecodedPictureOutputScheduler().Enqueue(
                Surface(sps, 1), sps, Slice(0, 0), 1, 0));
    }

    [Fact]
    public void Enqueue_ShouldRejectCompletePixelsWithoutDeblockingMetadata()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(0);
        H264DecodedPictureSurface surface = new(sps);
        surface.WriteMacroblock(0, 0, 0, false,
            new byte[256], new byte[64], new byte[64]);

        Assert.Throws<InvalidOperationException>(() =>
            new H264DecodedPictureOutputScheduler().Enqueue(
                surface, sps, Slice(0, 0), 1));
    }

    [Fact]
    public void Enqueue_WhenDeblockingMetadataIsMissing_ShouldNotAdvancePocHistory()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(0) with
        {
            PicOrderCntType = 2
        };
        H264DecodedPictureOutputScheduler scheduler = new();
        H264DecodedPictureSurface invalid = new(sps);
        invalid.WriteMacroblock(0, 0, 0, false,
            new byte[256], new byte[64], new byte[64]);

        // This non-reference picture would advance frame_num history to 15
        // if POC were calculated before the missing metadata was rejected.
        Assert.Throws<InvalidOperationException>(() =>
            scheduler.Enqueue(invalid, sps, Slice(15, 0), 0));

        H264DecodedPictureOutput output = Assert.Single(scheduler.Enqueue(
            Surface(sps, 22), sps, Slice(0, 0), 1));

        // With untouched history, frame_num 0 remains in the initial epoch.
        Assert.Equal(0, output.PictureOrderCount);
    }

    [Fact]
    public void Enqueue_ShouldRejectIncompletePictureSurfaces()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(0);
        H264DecodedPictureSurface incomplete = new(sps);

        Assert.Throws<InvalidOperationException>(() =>
            new H264DecodedPictureOutputScheduler().Enqueue(
                incomplete, sps, Slice(0, 0), 1));
    }

    [Fact]
    public void Enqueue_ShouldRequireFallbackWhenVuiDoesNotSignalReorderLimit()
    {
        H264SequenceParameterSet sps = new(
            0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);
        Assert.Throws<NotSupportedException>(() =>
            new H264DecodedPictureOutputScheduler().Enqueue(
                Surface(sps, 1), sps, Slice(0, 0), 1));
        H264DecodedPictureOutput immediate = Assert.Single(
            new H264DecodedPictureOutputScheduler().Enqueue(
                Surface(sps, 1), sps, Slice(0, 0), 1, 0));
        Assert.Equal(0, immediate.PictureOrderCount);
    }

    private static H264SequenceParameterSet SpsWithReorderLimit(
        uint reorderLimit, uint maxDecFrameBuffering = 4)
    {
        H264VuiParameters vui = new(
            1, null, null, false, null, null, null, null, null,
            null, null, null, null, false, null, null, false, false,
            true, false, 0, 0, 0, 0, reorderLimit, maxDecFrameBuffering);
        return new H264SequenceParameterSet(
            0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, true)
        { VuiParameters = vui };
    }

    private static H264DecodedPictureSurface Surface(H264SequenceParameterSet sps, byte value)
    {
        H264DecodedPictureSurface surface = new(sps);
        surface.WriteMacroblock(0, 0, 0, false,
            Enumerable.Repeat(value, 256).ToArray(),
            Enumerable.Repeat(value, 64).ToArray(),
            Enumerable.Repeat(value, 64).ToArray());
        H264DeblockingBlockMetadata intra =
            new(true, false, null, null, null, null);
        surface.SetDeblockingMetadata(0, 0,
            new H264DeblockingMacroblockMetadata(
                30, 30, 30, 0, 0, 0, true,
                Enumerable.Repeat(intra, 16).ToArray(),
                Enumerable.Repeat(intra, 4).ToArray(),
                Enumerable.Repeat(intra, 4).ToArray()));
        return surface;
    }

    private static H264SliceHeader Slice(
        uint frameNum, uint pocLsb, bool idr = false, bool noOutputPrior = false)
    {
        H264DecodedReferenceMarking? marking = idr
            ? new H264DecodedReferenceMarking(noOutputPrior, false, false, [])
            : null;
        return new H264SliceHeader(
            0, idr ? H264SliceType.I : H264SliceType.P, 0, 0, frameNum,
            false, false, idr ? 0u : null, pocLsb,
            null, null, null, null,
            false, false, 0, 0, [], [], null, marking,
            null, 0, false, null, null, null, null, null);
    }
}
