using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DecodedPictureOutputSchedulerTests
{
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
