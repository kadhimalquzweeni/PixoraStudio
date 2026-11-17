using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Timeline;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4AudioSeekPlannerTests
{
    private readonly Mp4AudioSeekPlanner _planner = new();

    [Fact]
    public void Plan_ShouldSelectSampleContainingTargetTime()
    {
        Mp4MediaTrack track = CreateTrack();
        Mp4AudioSeekPlan result = _planner.Plan(
            track,
            CreateSamples(track),
            1.01,
            48000);

        Assert.Equal(2u, result.TargetSampleNumber);
        Assert.Equal(1u, result.FirstSampleToDecode);
        Assert.Equal(480, result.FramesToSkipInTargetSample);
        Assert.False(result.IsAtEndOfTrack);
    }

    [Fact]
    public void Plan_ShouldIncludePreviousDecodeSampleAsAacPreroll()
    {
        Mp4MediaTrack track = CreateTrack();
        Mp4AudioSeekPlan result = _planner.Plan(
            track,
            CreateSamples(track),
            2.1,
            48000);

        Assert.Equal(3u, result.TargetSampleNumber);
        Assert.Equal(2u, result.FirstSampleToDecode);
    }

    [Fact]
    public void Plan_ShouldSelectFirstSampleForSeekBeforeFirstPresentationSample()
    {
        Mp4MediaTrack track = CreateTrack();
        Mp4AudioSeekPlan result = _planner.Plan(
            track,
            CreateSamples(track),
            0,
            48000);

        Assert.Equal(1u, result.TargetSampleNumber);
        Assert.Equal(1u, result.FirstSampleToDecode);
        Assert.Equal(0, result.FramesToSkipInTargetSample);
    }

    [Fact]
    public void Plan_ShouldSelectNextSampleWhenTargetFallsInPresentationGap()
    {
        Mp4MediaTrack track = CreateTrack();
        Mp4PresentationSample[] samples =
        [
            CreateSample(track.TrackId, 1, 0, 500),
            CreateSample(track.TrackId, 2, 1, 500)
        ];

        Mp4AudioSeekPlan result = _planner.Plan(track, samples, 0.75, 48000);

        Assert.Equal(2u, result.TargetSampleNumber);
        Assert.Equal(0, result.FramesToSkipInTargetSample);
    }

    [Fact]
    public void Plan_ShouldClampSeekAfterTrackEndToFinalSample()
    {
        Mp4MediaTrack track = CreateTrack();
        Mp4AudioSeekPlan result = _planner.Plan(
            track,
            CreateSamples(track),
            10,
            48000);

        Assert.Equal(3u, result.TargetSampleNumber);
        Assert.Equal(2u, result.FirstSampleToDecode);
        Assert.Equal(1024, result.FramesToSkipInTargetSample);
        Assert.True(result.IsAtEndOfTrack);
    }

    [Fact]
    public void Plan_ShouldFilterPresentationSamplesByTrack()
    {
        Mp4MediaTrack track = CreateTrack();
        Mp4PresentationSample[] samples =
        [
            CreateSample(99, 1, 0, 1000),
            .. CreateSamples(track)
        ];

        Mp4AudioSeekPlan result = _planner.Plan(track, samples, 0.1, 48000);

        Assert.Equal(1u, result.TargetSampleNumber);
    }

    [Fact]
    public void Plan_ShouldSortSamplesByPresentationTime()
    {
        Mp4MediaTrack track = CreateTrack();
        Mp4PresentationSample[] samples =
        [
            CreateSample(track.TrackId, 2, 1, 1000),
            CreateSample(track.TrackId, 1, 0, 1000)
        ];

        Mp4AudioSeekPlan result = _planner.Plan(track, samples, 0.1, 48000);

        Assert.Equal(1u, result.TargetSampleNumber);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Plan_ShouldRejectInvalidTargetTime(double targetTime)
    {
        Mp4MediaTrack track = CreateTrack();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _planner.Plan(track, CreateSamples(track), targetTime, 48000));
    }

    [Theory]
    [InlineData(0, 1024)]
    [InlineData(48000, 0)]
    public void Plan_ShouldRejectInvalidAudioFormat(int sampleRate, int frameCount)
    {
        Mp4MediaTrack track = CreateTrack();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _planner.Plan(track, CreateSamples(track), 0.1, sampleRate, frameCount));
    }

    [Fact]
    public void Plan_ShouldAcceptValidAudioFormat()
    {
        Mp4MediaTrack track = CreateTrack();

        Mp4AudioSeekPlan result = _planner.Plan(
            track,
            CreateSamples(track),
            0.1,
            48000,
            1024);

        Assert.Equal(1u, result.TargetSampleNumber);
        Assert.Equal(1u, result.FirstSampleToDecode);
    }

    [Fact]
    public void Plan_ShouldRejectMalformedSampleTiming()
    {
        Mp4MediaTrack track = CreateTrack();
        Mp4PresentationSample invalid = new(
            track.TrackId,
            1,
            new Mp4SampleLocation(1, 1, 0, 1, 0, 0, true),
            0);

        Assert.Throws<Mp4ParsingException>(() =>
            _planner.Plan(track, [invalid], 0, 48000));
    }

    [Fact]
    public void Plan_ShouldRejectNonAudioTrack()
    {
        Mp4MediaTrack track = CreateTrack(Mp4HandlerType.Video);

        Assert.Throws<ArgumentException>(() =>
            _planner.Plan(track, CreateSamples(track), 0.1, 48000));
    }

    [Fact]
    public void Plan_ShouldRejectEmptyPresentationTimeline()
    {
        Mp4MediaTrack track = CreateTrack();

        Assert.Throws<Mp4ParsingException>(() =>
            _planner.Plan(track, [], 0.1, 48000));
    }

    private static Mp4MediaTrack CreateTrack(
        Mp4HandlerType handlerType = Mp4HandlerType.Audio)
    {
        Mp4Track track = new(
            1, 0, 0, 1000, 3000, handlerType,
            [new Mp4SampleDescription(1, "mp4a")]);

        Mp4SampleTable table = new(
            [], [],
            new Mp4SampleSizeTable(1, 0, []),
            new Mp4ChunkOffsetTable([]),
            null);

        return new Mp4MediaTrack(
            track,
            new Mp4Box(8, "trak", 0, 8),
            table,
            new Mp4SampleLocationTable(
            [
                new Mp4SampleLocation(1, 1, 0, 1, 0, 1000, true),
                new Mp4SampleLocation(2, 1, 1, 1, 1000, 1000, false),
                new Mp4SampleLocation(3, 1, 2, 1, 2000, 1000, false)
            ]));
    }

    private static Mp4PresentationSample[] CreateSamples(Mp4MediaTrack track) =>
    [
        CreateSample(track.TrackId, 1, 0, 1000),
        CreateSample(track.TrackId, 2, 1, 1000),
        CreateSample(track.TrackId, 3, 2, 1000)
    ];

    private static Mp4PresentationSample CreateSample(
        uint trackId,
        uint sampleNumber,
        double presentationTime,
        uint durationTicks)
    {
        Mp4SampleLocation location = new(
            sampleNumber, 1, 0, 1,
            checked((ulong)(sampleNumber - 1) * durationTicks),
            durationTicks,
            sampleNumber == 1);

        return new Mp4PresentationSample(
            trackId,
            sampleNumber,
            location,
            presentationTime);
    }
}
