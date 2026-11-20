using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4FrameNavigationPlannerTests
{
    private readonly H264Mp4FrameNavigationPlanner _planner = new();

    [Fact]
    public void FindAdjacentFrame_Next_ShouldUseNextPresentationTimestamp()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 40),
            Sample(2, 40, 40),
            Sample(3, 80, 40),
            Sample(4, 120, 40));

        H264Mp4FrameNavigationTarget? target = _planner.FindAdjacentFrame(
            track, 0.04, H264Mp4FrameNavigationDirection.Next);

        Assert.NotNull(target);
        Assert.Equal(3u, target.SampleNumber);
        Assert.Equal(80L, target.PresentationTimeTicks);
        Assert.Equal(0.08, target.PresentationTimeSeconds);
    }

    [Fact]
    public void FindAdjacentFrame_Previous_ShouldUsePreviousPresentationTimestamp()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 40),
            Sample(2, 40, 40),
            Sample(3, 80, 40));

        H264Mp4FrameNavigationTarget? target = _planner.FindAdjacentFrame(
            track, 0.08, H264Mp4FrameNavigationDirection.Previous);

        Assert.NotNull(target);
        Assert.Equal(2u, target.SampleNumber);
        Assert.Equal(40L, target.PresentationTimeTicks);
    }

    [Fact]
    public void FindAdjacentFrame_VariableFrameRate_ShouldNotAssumeConstantDuration()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 20),
            Sample(2, 20, 80),
            Sample(3, 100, 15),
            Sample(4, 115, 50));

        H264Mp4FrameNavigationTarget? next = _planner.FindAdjacentFrame(
            track, 0.02, H264Mp4FrameNavigationDirection.Next);
        H264Mp4FrameNavigationTarget? previous = _planner.FindAdjacentFrame(
            track, 0.10, H264Mp4FrameNavigationDirection.Previous);

        Assert.NotNull(next);
        Assert.Equal(3u, next.SampleNumber);
        Assert.Equal(100L, next.PresentationTimeTicks);

        Assert.NotNull(previous);
        Assert.Equal(2u, previous.SampleNumber);
        Assert.Equal(20L, previous.PresentationTimeTicks);
    }

    [Fact]
    public void FindAdjacentFrame_BFrames_ShouldSortByPresentationTimeNotDecodeOrder()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 40, compositionOffset: 0),
            Sample(2, 40, 40, compositionOffset: 80),
            Sample(3, 80, 40, compositionOffset: -40),
            Sample(4, 120, 40, compositionOffset: 0));

        H264Mp4FrameNavigationTarget? next = _planner.FindAdjacentFrame(
            track, 0.0, H264Mp4FrameNavigationDirection.Next);
        H264Mp4FrameNavigationTarget? previous = _planner.FindAdjacentFrame(
            track, 0.08, H264Mp4FrameNavigationDirection.Previous);

        Assert.NotNull(next);
        Assert.Equal(3u, next.SampleNumber);
        Assert.Equal(40L, next.PresentationTimeTicks);

        Assert.NotNull(previous);
        Assert.Equal(3u, previous.SampleNumber);
        Assert.Equal(40L, previous.PresentationTimeTicks);
    }

    [Fact]
    public void FindAdjacentFrame_DuplicatePresentationTimestamps_ShouldUseSampleNumberAsTieBreaker()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 40),
            Sample(2, 40, 40),
            Sample(3, 80, 40, compositionOffset: -40),
            Sample(4, 120, 40));

        H264Mp4FrameNavigationTarget? previous = _planner.FindAdjacentFrame(
            track, 0.08, H264Mp4FrameNavigationDirection.Previous);

        Assert.NotNull(previous);
        Assert.Equal(3u, previous.SampleNumber);
        Assert.Equal(40L, previous.PresentationTimeTicks);
    }

    [Fact]
    public void FindAdjacentFrame_AtFirstFramePrevious_ShouldReturnNull()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 40),
            Sample(2, 40, 40));

        Assert.Null(_planner.FindAdjacentFrame(
            track, 0, H264Mp4FrameNavigationDirection.Previous));
    }

    [Fact]
    public void FindAdjacentFrame_AtLastFrameNext_ShouldReturnNull()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 40),
            Sample(2, 40, 40));

        Assert.Null(_planner.FindAdjacentFrame(
            track, 0.04, H264Mp4FrameNavigationDirection.Next));
    }

    [Fact]
    public void FindAdjacentFrame_OffFramePosition_ShouldFindNearestStrictlyAdjacentFrame()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 40),
            Sample(2, 40, 40),
            Sample(3, 80, 40));

        H264Mp4FrameNavigationTarget? previous = _planner.FindAdjacentFrame(
            track, 0.06, H264Mp4FrameNavigationDirection.Previous);
        H264Mp4FrameNavigationTarget? next = _planner.FindAdjacentFrame(
            track, 0.06, H264Mp4FrameNavigationDirection.Next);

        Assert.Equal(2u, previous?.SampleNumber);
        Assert.Equal(3u, next?.SampleNumber);
    }

    [Fact]
    public void FindAdjacentFrame_RepeatedNavigation_ShouldAdvanceFromEachSelectedFrame()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 40),
            Sample(2, 40, 40),
            Sample(3, 80, 40),
            Sample(4, 120, 40));

        H264Mp4FrameNavigationTarget? first = _planner.FindAdjacentFrame(
            track, 0, H264Mp4FrameNavigationDirection.Next);
        H264Mp4FrameNavigationTarget? second = _planner.FindAdjacentFrame(
            track, first!.PresentationTimeSeconds, H264Mp4FrameNavigationDirection.Next);
        H264Mp4FrameNavigationTarget? previous = _planner.FindAdjacentFrame(
            track, second!.PresentationTimeSeconds, H264Mp4FrameNavigationDirection.Previous);

        Assert.Equal(2u, first.SampleNumber);
        Assert.Equal(3u, second.SampleNumber);
        Assert.Equal(2u, previous?.SampleNumber);
    }

    [Fact]
    public void FindAdjacentFrame_PreviousMayReturnNegativePresentationTimestamp()
    {
        Mp4MediaTrack track = CreateTrack(
            Sample(1, 0, 40, compositionOffset: -20),
            Sample(2, 40, 40));

        H264Mp4FrameNavigationTarget? previous = _planner.FindAdjacentFrame(
            track, 0, H264Mp4FrameNavigationDirection.Previous);

        Assert.NotNull(previous);
        Assert.Equal(1u, previous.SampleNumber);
        Assert.Equal(-20L, previous.PresentationTimeTicks);
        Assert.Equal(-0.02, previous.PresentationTimeSeconds);
    }

    [Fact]
    public void FindAdjacentFrame_ShouldRejectAudioTrack()
    {
        Mp4MediaTrack track = CreateTrack(
            Mp4HandlerType.Audio,
            1000,
            Sample(1, 0, 40));

        Assert.Throws<ArgumentException>(() => _planner.FindAdjacentFrame(
            track, 0, H264Mp4FrameNavigationDirection.Next));
    }

    [Fact]
    public void FindAdjacentFrame_ShouldRejectZeroTimescale()
    {
        Mp4MediaTrack track = CreateTrack(
            Mp4HandlerType.Video,
            0,
            Sample(1, 0, 40));

        Assert.Throws<FormatException>(() => _planner.FindAdjacentFrame(
            track, 0, H264Mp4FrameNavigationDirection.Next));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FindAdjacentFrame_ShouldRejectInvalidCurrentTime(double currentTime)
    {
        Mp4MediaTrack track = CreateTrack(Sample(1, 0, 40));

        Assert.Throws<ArgumentOutOfRangeException>(() => _planner.FindAdjacentFrame(
            track, currentTime, H264Mp4FrameNavigationDirection.Next));
    }

    [Fact]
    public void FindAdjacentFrame_ShouldRejectInvalidDirection()
    {
        Mp4MediaTrack track = CreateTrack(Sample(1, 0, 40));

        Assert.Throws<ArgumentOutOfRangeException>(() => _planner.FindAdjacentFrame(
            track, 0, (H264Mp4FrameNavigationDirection)99));
    }

    private static Mp4MediaTrack CreateTrack(params Mp4SampleLocation[] samples) =>
        CreateTrack(Mp4HandlerType.Video, 1000, samples);

    private static Mp4MediaTrack CreateTrack(
        Mp4HandlerType handlerType,
        uint timescale,
        params Mp4SampleLocation[] samples)
    {
        Mp4Track track = new(7, 1920, 1080, timescale, 0, handlerType, []);
        Mp4SampleTable table = new(
            [], [],
            new Mp4SampleSizeTable(1, 0, []),
            new Mp4ChunkOffsetTable([]),
            null);

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
        int compositionOffset = 0) =>
        new(number, 1, 0, 1, decodeTime, duration, true)
        {
            CompositionOffsetTicks = compositionOffset
        };
}
