using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Export;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4TrimExportPlanBuilderTests
{
    private readonly Mp4TrimExportPlanBuilder _builder = new();

    [Fact]
    public void Build_ShouldRetainDecodePrerollAndUseFrameAlignedPresentationRange()
    {
        Mp4MediaInfo info = CreateMediaInfo(videoSyncSamples: [1, 7, 10]);

        Mp4TrimExportPlan plan = _builder.Build(info, 1.2, 2.2);

        Mp4TrimTrackPlan video = Assert.Single(plan.Tracks, track => track.HandlerType == Mp4HandlerType.Video);
        Assert.Equal(1u, video.FirstSampleNumber);
        Assert.Equal(6u, video.LastSampleNumber);
        Assert.Equal(1.5, plan.EffectiveStartSeconds);
        Assert.Equal(2.5, plan.EffectiveEndSeconds);
    }

    [Fact]
    public void Build_ShouldIncludeOnlyAudioPacketsOverlappingFrameAccurateRange()
    {
        Mp4TrimExportPlan plan = _builder.Build(CreateMediaInfo(), 1.2, 2.2);

        Mp4TrimTrackPlan audio = Assert.Single(plan.Tracks, track => track.HandlerType == Mp4HandlerType.Audio);
        Assert.Equal(new uint[] { 4, 5 }, audio.Samples.Select(sample => sample.SampleNumber));
    }

    [Fact]
    public void Build_ShouldUseLaterSyncSampleWhenNoEarlierSyncExists()
    {
        Mp4MediaInfo info = CreateMediaInfo(videoSyncSamples: [3, 6]);

        Mp4TrimExportPlan plan = _builder.Build(info, 0.2, 1.2);

        Mp4TrimTrackPlan video = Assert.Single(plan.Tracks, track => track.HandlerType == Mp4HandlerType.Video);
        Assert.Equal(3u, video.FirstSampleNumber);
        Assert.Equal(1.0, plan.EffectiveStartSeconds);
        Assert.Equal(1.5, plan.EffectiveEndSeconds);
    }

    [Fact]
    public void Build_ShouldUseEndOfTrackWhenNoLaterSyncBoundaryExists()
    {
        Mp4TrimExportPlan plan = _builder.Build(CreateMediaInfo(), 3.2, 4.7);

        Mp4TrimTrackPlan video = Assert.Single(plan.Tracks, track => track.HandlerType == Mp4HandlerType.Video);
        Assert.Equal(7u, video.FirstSampleNumber);
        Assert.Equal(10u, video.LastSampleNumber);
        Assert.Equal(3.5, plan.EffectiveStartSeconds);
        Assert.Equal(5.0, plan.EffectiveEndSeconds);
    }

    [Fact]
    public void Build_ShouldUsePresentationTimestampsRatherThanDecodeOrderForBFrames()
    {
        Mp4MediaTrack video = CreateTrack(
            1,
            Mp4HandlerType.Video,
            [
                Location(1, 0, 500, true),
                Location(2, 500, 500, false),
                Location(3, 1000, 500, false),
                Location(4, 1500, 500, false),
                Location(5, 2000, 500, true),
                Location(6, 2500, 500, false)
            ]);

        // Decode order is 0, 1.5, 0.5, 1.0, 2.0, 2.5 seconds.
        // Presentation order differs because samples 3 and 4 are B pictures.
        Mp4PresentationSample[] timeline =
        [
            new(1, 1, video.SampleLocations.GetSample(1), 0.0),
            new(1, 2, video.SampleLocations.GetSample(2), 1.5),
            new(1, 3, video.SampleLocations.GetSample(3), 0.5),
            new(1, 4, video.SampleLocations.GetSample(4), 1.0),
            new(1, 5, video.SampleLocations.GetSample(5), 2.0),
            new(1, 6, video.SampleLocations.GetSample(6), 2.5)
        ];

        Mp4MediaInfo info = new(
            new Mp4MovieHeader(1000, 3000),
            [video],
            [new Mp4MediaData(0, 1000)],
            timeline);

        Mp4TrimExportPlan plan = _builder.Build(info, 0.6, 1.6);

        Mp4TrimTrackPlan selected = Assert.Single(plan.Tracks);
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, selected.Samples.Select(sample => sample.SampleNumber));
        Assert.Equal(1.0, plan.EffectiveStartSeconds);
        Assert.Equal(2.0, plan.EffectiveEndSeconds);
    }

    [Fact]
    public void Build_ShouldRejectMissingVideoTrack()
    {
        Mp4MediaInfo info = new(
            new Mp4MovieHeader(1000, 5000),
            [CreateTrack(2, Mp4HandlerType.Audio, Enumerable.Range(1, 10).Select(i => Location((uint)i, (ulong)((i - 1) * 500), 500, true)))],
            [new Mp4MediaData(0, 100)],
            []);

        Assert.Throws<NotSupportedException>(() => _builder.Build(info, 0, 1));
    }

    [Theory]
    [InlineData(-0.1, 1)]
    [InlineData(double.NaN, 1)]
    [InlineData(1, 1)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(0, 6)]
    public void Build_ShouldRejectInvalidTrimRange(double start, double end)
    {
        Assert.ThrowsAny<ArgumentException>(() => _builder.Build(CreateMediaInfo(), start, end));
    }

    [Fact]
    public void Build_ShouldRejectSourceVideoEditListsUntilTheyAreSupported()
    {
        Mp4MediaTrack video = CreateTrack(
            1,
            Mp4HandlerType.Video,
            Enumerable.Range(1, 10).Select(i =>
                Location((uint)i, (ulong)((i - 1) * 500), 500, i == 1 || i == 4 || i == 7 || i == 10)),
            new Mp4EditList([new Mp4EditListEntry(500, 0, 1, 0)]));

        Mp4PresentationSample[] timeline = video.SampleLocations.Samples
            .Select(sample => new Mp4PresentationSample(1, sample.SampleNumber, sample, sample.DecodeTimeTicks / 1000.0))
            .ToArray();

        Mp4MediaInfo info = new(
            new Mp4MovieHeader(1000, 5000),
            [video],
            [new Mp4MediaData(0, 1000)],
            timeline);

        Assert.Throws<NotSupportedException>(() => _builder.Build(info, 0, 1));
    }

    [Fact]
    public void Build_ShouldRejectVideoWithoutSyncSamples()
    {
        Mp4MediaInfo info = CreateMediaInfo(videoSyncSamples: []);

        Assert.Throws<NotSupportedException>(() => _builder.Build(info, 0.2, 1.2));
    }

    private static Mp4MediaInfo CreateMediaInfo(uint[]? videoSyncSamples = null)
    {
        Mp4MediaTrack video = CreateTrack(
            1,
            Mp4HandlerType.Video,
            Enumerable.Range(1, 10).Select(i =>
                Location((uint)i, (ulong)((i - 1) * 500), 500,
                    (videoSyncSamples ?? [1, 4, 7, 10]).Contains((uint)i))));

        Mp4MediaTrack audio = CreateTrack(
            2,
            Mp4HandlerType.Audio,
            Enumerable.Range(1, 10).Select(i =>
                Location((uint)i, (ulong)((i - 1) * 500), 500, true)));

        Mp4PresentationSample[] timeline = video.SampleLocations.Samples
            .Select(sample => new Mp4PresentationSample(1, sample.SampleNumber, sample, sample.DecodeTimeTicks / 1000.0))
            .Concat(audio.SampleLocations.Samples.Select(sample =>
                new Mp4PresentationSample(2, sample.SampleNumber, sample, sample.DecodeTimeTicks / 1000.0)))
            .ToArray();

        return new Mp4MediaInfo(
            new Mp4MovieHeader(1000, 5000),
            [video, audio],
            [new Mp4MediaData(0, 1000)],
            timeline);
    }

    private static Mp4MediaTrack CreateTrack(
        uint id,
        Mp4HandlerType type,
        IEnumerable<Mp4SampleLocation> samples,
        Mp4EditList? editList = null)
    {
        Mp4SampleLocation[] locations = samples.ToArray();
        Mp4Track track = new(id, 1920, 1080, 1000, 5000, type,
            [new Mp4SampleDescription(1, type == Mp4HandlerType.Video ? "avc1" : "mp4a")]);
        Mp4SampleTable table = new(
            [new Mp4TimeToSampleEntry((uint)locations.Length, 500)],
            [new Mp4SampleToChunkEntry(1, 1, 1)],
            new Mp4SampleSizeTable(1, (uint)locations.Length, []),
            new Mp4ChunkOffsetTable([0]),
            null);
        return new Mp4MediaTrack(track, new Mp4Box(8, "trak", 0, 8), table,
            new Mp4SampleLocationTable(locations), editList);
    }

    private static Mp4SampleLocation Location(uint number, ulong dts, uint duration, bool sync) =>
        new(number, 1, 0, 1, dts, duration, sync) { SampleDescriptionIndex = 1 };
}
