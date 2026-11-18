using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4VideoOutputSchedulerTests
{
    [Fact]
    public void Enqueue_ShouldKeepMp4TimestampsAttachedWhenPocReordersFrames()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(1);
        H264Mp4VideoOutputScheduler scheduler = new();
        H264SliceHeader firstSlice = Slice(0, 2);
        H264SliceHeader secondSlice = Slice(1, 0);
        H264Mp4VideoSample firstSample = Sample(1, 100, 200);
        H264Mp4VideoSample secondSample = Sample(2, 140, 160);

        H264PictureOrderCount firstPoc =
            scheduler.CalculatePictureOrderCount(sps, firstSlice, 1);
        Assert.Empty(scheduler.Enqueue(
            Surface(sps, 10), firstSample, sps, firstSlice, 1, firstPoc));

        H264PictureOrderCount secondPoc =
            scheduler.CalculatePictureOrderCount(sps, secondSlice, 1);
        H264Mp4DecodedVideoFrame reordered = Assert.Single(scheduler.Enqueue(
            Surface(sps, 20), secondSample, sps, secondSlice, 1, secondPoc));

        Assert.Equal(0, reordered.PictureOrderCount);
        Assert.Equal(2u, reordered.SampleNumber);
        Assert.Equal(140UL, reordered.DecodeTimeTicks);
        Assert.Equal(160L, reordered.PresentationTimeTicks);
        Assert.Equal(1000u, reordered.Timescale);

        H264Mp4DecodedVideoFrame remaining = Assert.Single(scheduler.Flush());
        Assert.Equal(2, remaining.PictureOrderCount);
        Assert.Equal(1u, remaining.SampleNumber);
        Assert.Equal(100UL, remaining.DecodeTimeTicks);
        Assert.Equal(200L, remaining.PresentationTimeTicks);
        Assert.Equal(0, scheduler.PendingSampleCount);
    }

    [Fact]
    public void Enqueue_IdrNoOutputPrior_ShouldDiscardPriorTimestampAssociations()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(2);
        H264Mp4VideoOutputScheduler scheduler = new();
        H264SliceHeader priorSlice = Slice(0, 2);
        H264PictureOrderCount priorPoc =
            scheduler.CalculatePictureOrderCount(sps, priorSlice, 1);
        scheduler.Enqueue(
            Surface(sps, 10), Sample(1, 0, 0), sps,
            priorSlice, 1, priorPoc);

        H264SliceHeader idrSlice = Slice(0, 0, idr: true, noOutputPrior: true);
        H264PictureOrderCount idrPoc =
            scheduler.CalculatePictureOrderCount(sps, idrSlice, 3);
        IReadOnlyList<H264Mp4DecodedVideoFrame> ready = scheduler.Enqueue(
            Surface(sps, 30), Sample(2, 40, 40), sps,
            idrSlice, 3, idrPoc);

        Assert.Empty(ready);
        Assert.Equal(1, scheduler.PendingCount);
        Assert.Equal(1, scheduler.PendingSampleCount);

        H264Mp4DecodedVideoFrame output = Assert.Single(scheduler.Flush());
        Assert.Equal(2u, output.SampleNumber);
        Assert.Equal(0, output.PictureOrderCount);
        Assert.Equal(0, scheduler.PendingSampleCount);
    }

    [Fact]
    public void Enqueue_IdrWithOutputPrior_ShouldReleasePriorSampleWithItsOwnTimestamps()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(2);
        H264Mp4VideoOutputScheduler scheduler = new();
        H264SliceHeader priorSlice = Slice(0, 2);
        H264PictureOrderCount priorPoc =
            scheduler.CalculatePictureOrderCount(sps, priorSlice, 1);
        scheduler.Enqueue(
            Surface(sps, 10), Sample(1, 100, 200), sps,
            priorSlice, 1, priorPoc);

        H264SliceHeader idrSlice = Slice(0, 0, idr: true, noOutputPrior: false);
        H264PictureOrderCount idrPoc =
            scheduler.CalculatePictureOrderCount(sps, idrSlice, 3);
        H264Mp4DecodedVideoFrame prior = Assert.Single(scheduler.Enqueue(
            Surface(sps, 30), Sample(2, 140, 160), sps,
            idrSlice, 3, idrPoc));

        Assert.Equal(1u, prior.SampleNumber);
        Assert.Equal(200L, prior.PresentationTimeTicks);
        Assert.Equal(2, prior.PictureOrderCount);

        H264Mp4DecodedVideoFrame current = Assert.Single(scheduler.Flush());
        Assert.Equal(2u, current.SampleNumber);
        Assert.Equal(0, current.PictureOrderCount);
    }

    [Fact]
    public void Reset_ShouldClearPocQueueAndSampleAssociations()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(2);
        H264Mp4VideoOutputScheduler scheduler = new();
        H264SliceHeader slice = Slice(0, 2);
        H264PictureOrderCount poc = scheduler.CalculatePictureOrderCount(sps, slice, 1);
        scheduler.Enqueue(Surface(sps, 10), Sample(1, 0, 0), sps, slice, 1, poc);

        scheduler.Reset();

        Assert.Equal(0, scheduler.PendingCount);
        Assert.Equal(0, scheduler.PendingSampleCount);
        Assert.Empty(scheduler.Flush());
    }

    [Fact]
    public void Enqueue_ShouldRejectSampleWithoutTrackTimescale()
    {
        H264SequenceParameterSet sps = SpsWithReorderLimit(0);
        H264Mp4VideoOutputScheduler scheduler = new();
        H264SliceHeader slice = Slice(0, 0);
        H264PictureOrderCount poc = scheduler.CalculatePictureOrderCount(sps, slice, 1);
        H264Mp4VideoSample invalidSample = Sample(1, 0, 0) with { Timescale = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Enqueue(
            Surface(sps, 10), invalidSample, sps, slice, 1, poc));
        Assert.Equal(0, scheduler.PendingSampleCount);
    }

    private static H264Mp4VideoSample Sample(
        uint sampleNumber, ulong decodeTimeTicks, long presentationTimeTicks) =>
        new(
            5, sampleNumber, decodeTimeTicks, presentationTimeTicks,
            40, 1000, true,
            new H264AvcDecoderConfiguration(66, 0, 30, 4, [], [], []),
            []);

    private static H264SequenceParameterSet SpsWithReorderLimit(uint reorderLimit)
    {
        H264VuiParameters vui = new(
            1, null, null, false, null, null, null, null, null,
            null, null, null, null, false, null, null, false, false,
            true, false, 0, 0, 0, 0, reorderLimit, 4);
        return new H264SequenceParameterSet(
            0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, true)
        { VuiParameters = vui };
    }

    private static H264DecodedPictureSurface Surface(
        H264SequenceParameterSet sps, byte value)
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
