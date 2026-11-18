using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4VideoSeekPlannerTests
{
    private readonly H264Mp4VideoSeekPlanner _planner = new();

    [Fact]
    public void Plan_ShouldStartAtLatestSyncSampleNotAfterTargetPresentationTime()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 1000, true),
            Sample(2, 1000, 1000, false),
            Sample(3, 2000, 1000, true),
            Sample(4, 3000, 1000, false),
            Sample(5, 4000, 1000, true));

        H264Mp4VideoSeekPlan plan = _planner.Plan(track, 3.5);

        Assert.Equal(3u, plan.FirstSampleToDecode);
        Assert.Equal(3500L, plan.TargetPresentationTimeTicks);
        Assert.Equal(1000u, plan.Timescale);
        Assert.Equal(3.5, plan.TargetPresentationTimeSeconds);
    }

    [Fact]
    public void Plan_ShouldUsePresentationTimeIncludingCompositionOffsets()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 1000, true),
            Sample(2, 1000, 1000, false, compositionOffset: 2000),
            Sample(3, 2000, 1000, true, compositionOffset: 1000),
            Sample(4, 3000, 1000, false, compositionOffset: 0));

        H264Mp4VideoSeekPlan plan = _planner.Plan(track, 3.5);

        // Sample 3 is a sync sample at PTS 3 seconds even though it is later
        // in decode order than sample 2, whose PTS is also 3 seconds.
        Assert.Equal(3u, plan.FirstSampleToDecode);
        Assert.Equal(3500L, plan.TargetPresentationTimeTicks);
    }

    [Fact]
    public void Plan_ShouldUseFirstSyncSampleWhenTargetPrecedesTrack()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 1000, true, compositionOffset: 500),
            Sample(2, 1000, 1000, false),
            Sample(3, 2000, 1000, true));

        H264Mp4VideoSeekPlan plan = _planner.Plan(track, 0);

        Assert.Equal(1u, plan.FirstSampleToDecode);
        Assert.Equal(0L, plan.TargetPresentationTimeTicks);
    }

    [Fact]
    public void Plan_ShouldClampTargetBeyondTrackEndToLastPresentationTick()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 1000, true),
            Sample(2, 1000, 1000, false),
            Sample(3, 2000, 500, true));

        H264Mp4VideoSeekPlan plan = _planner.Plan(track, 100);

        Assert.Equal(3u, plan.FirstSampleToDecode);
        Assert.Equal(2499L, plan.TargetPresentationTimeTicks);
    }

    [Fact]
    public void Plan_ShouldSelectSyncSampleByPresentationTimeRatherThanSampleNumber()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 1000, true),
            Sample(2, 1000, 1000, true, compositionOffset: 3000),
            Sample(3, 2000, 1000, false),
            Sample(4, 3000, 1000, false));

        H264Mp4VideoSeekPlan plan = _planner.Plan(track, 2.5);

        Assert.Equal(1u, plan.FirstSampleToDecode);
    }

    [Fact]
    public void Plan_ShouldRejectTrackWithoutSyncSamples()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 1000, false),
            Sample(2, 1000, 1000, false));

        Assert.Throws<NotSupportedException>(() => _planner.Plan(track, 0.5));
    }

    [Fact]
    public void Plan_ShouldRejectAudioTrack()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 1000, true),
            handlerType: Mp4HandlerType.Audio);

        Assert.Throws<ArgumentException>(() => _planner.Plan(track, 0));
    }

    [Fact]
    public void Plan_ShouldRejectEmptyTrack()
    {
        Mp4MediaTrack track = CreateTrack([]);

        Assert.Throws<FormatException>(() => _planner.Plan(track, 0));
    }

    [Fact]
    public void Plan_ShouldRejectZeroTimescale()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 1000, true),
            timescale: 0);

        Assert.Throws<FormatException>(() => _planner.Plan(track, 0));
    }

    [Fact]
    public void Plan_ShouldRejectZeroDurationSample()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 0, true));

        Assert.Throws<FormatException>(() => _planner.Plan(track, 0));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Plan_ShouldRejectInvalidTargetTime(double target)
    {
        Mp4MediaTrack track = CreateTrack(Sample(1, 0, 1000, true));

        Assert.Throws<ArgumentOutOfRangeException>(() => _planner.Plan(track, target));
    }

    private static Mp4MediaTrack CreateTrack(
        IEnumerable<Mp4SampleLocation> samples,
        Mp4HandlerType handlerType = Mp4HandlerType.Video,
        uint timescale = 1000)
    {
        Mp4Track track = new(
            7, 1920, 1080, timescale, 0, handlerType, []);

        Mp4SampleTable table = new([], [],
            new Mp4SampleSizeTable(1, 0, []),
            new Mp4ChunkOffsetTable([]), null);

        return new Mp4MediaTrack(
            track,
            new Mp4Box(8, "trak", 0, 8),
            table,
            new Mp4SampleLocationTable(samples));
    }

    private static Mp4SampleLocation Sample(
        uint number,
        ulong decodeTime,
        uint duration,
        bool sync,
        int compositionOffset = 0) =>
        new(number, 1, 0, 1, decodeTime, duration, sync)
        {
            CompositionOffsetTicks = compositionOffset
        };
}
