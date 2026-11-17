
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Timeline;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4PresentationTimelineBuilderTests
{
    private readonly Mp4PresentationTimelineBuilder _builder = new();

    [Fact]
    public void Build_ShouldConvertTrackTimescaleToSeconds()
    {
        Mp4MediaTrack track = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, decodeTime: 0, duration: 500),
                CreateLocation(2, decodeTime: 500, duration: 500),
                CreateLocation(3, decodeTime: 1000, duration: 500)
            ]);

        IReadOnlyList<Mp4PresentationSample> result =
            _builder.Build(
                new Mp4MovieHeader(1000, 1500),
                [track]);

        Assert.Equal(3, result.Count);
        Assert.Equal(0.0, result[0].PresentationTimeSeconds);
        Assert.Equal(0.5, result[1].PresentationTimeSeconds);
        Assert.Equal(1.0, result[2].PresentationTimeSeconds);
    }

    [Fact]
    public void Build_ShouldSortSamplesByPresentationTime()
    {
        Mp4MediaTrack firstTrack = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, 500, 500)
            ]);

        Mp4MediaTrack secondTrack = CreateTrack(
            trackId: 2,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, 0, 500)
            ]);

        IReadOnlyList<Mp4PresentationSample> result =
            _builder.Build(
                new Mp4MovieHeader(1000, 1000),
                [firstTrack, secondTrack]);

        Assert.Equal(2, result.Count);
        Assert.Equal(2u, result[0].TrackId);
        Assert.Equal(1u, result[1].TrackId);
    }

    [Fact]
    public void Build_ShouldUseCompositionOffsetForPresentationTime()
    {
        Mp4MediaTrack track = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(
                    1,
                    decodeTime: 1000,
                    duration: 500,
                    compositionOffset: 500)
            ]);

        IReadOnlyList<Mp4PresentationSample> result =
            _builder.Build(
                new Mp4MovieHeader(1000, 2000),
                [track]);

        Assert.Single(result);
        Assert.Equal(1.5, result[0].PresentationTimeSeconds);

        // Decode timestamp remains unchanged.
        Assert.Equal(1000ul, result[0].Location.DecodeTimeTicks);
    }

    [Fact]
    public void Build_ShouldPreserveSampleNumbersAndDecodeOrder()
    {
        Mp4MediaTrack track = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, 1000, 500, compositionOffset: 500),
                CreateLocation(2, 1500, 500, compositionOffset: -500)
            ]);

        IReadOnlyList<Mp4PresentationSample> result =
            _builder.Build(
                new Mp4MovieHeader(1000, 2000),
                [track]);

        Assert.Equal(2, result.Count);

        // The second sample is presented first, but its original
        // sample number and decode timestamp are preserved.
        Assert.Equal(2u, result[0].SampleNumber);
        Assert.Equal(1500ul, result[0].Location.DecodeTimeTicks);

        Assert.Equal(1u, result[1].SampleNumber);
        Assert.Equal(1000ul, result[1].Location.DecodeTimeTicks);
    }

    [Fact]
    public void Build_ShouldApplyMediaStartTime()
    {
        Mp4MediaTrack track = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, 0, 500),
                CreateLocation(2, 500, 500),
                CreateLocation(3, 1000, 500)
            ],
            editList:
            [
                new Mp4EditListEntry(1000, 500, 1, 0)
            ]);

        IReadOnlyList<Mp4PresentationSample> result =
            _builder.Build(
                new Mp4MovieHeader(1000, 1000),
                [track]);

        Assert.Equal(2, result.Count);

        Assert.Equal(2u, result[0].SampleNumber);
        Assert.Equal(0.0, result[0].PresentationTimeSeconds);

        Assert.Equal(3u, result[1].SampleNumber);
        Assert.Equal(0.5, result[1].PresentationTimeSeconds);
    }

    [Fact]
    public void Build_ShouldApplyEmptyEdit()
    {
        Mp4MediaTrack track = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, 0, 500),
                CreateLocation(2, 500, 500)
            ],
            editList:
            [
                new Mp4EditListEntry(500, -1, 1, 0),
                new Mp4EditListEntry(1000, 0, 1, 0)
            ]);

        IReadOnlyList<Mp4PresentationSample> result =
            _builder.Build(
                new Mp4MovieHeader(1000, 1500),
                [track]);

        Assert.Equal(2, result.Count);

        Assert.Equal(0.5, result[0].PresentationTimeSeconds);
        Assert.Equal(1.0, result[1].PresentationTimeSeconds);
    }

    [Fact]
    public void Build_ShouldExcludeSamplesOutsideEditSegment()
    {
        Mp4MediaTrack track = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, 0, 500),
                CreateLocation(2, 500, 500),
                CreateLocation(3, 1000, 500),
                CreateLocation(4, 1500, 500)
            ],
            editList:
            [
                new Mp4EditListEntry(1000, 500, 1, 0)
            ]);

        IReadOnlyList<Mp4PresentationSample> result =
            _builder.Build(
                new Mp4MovieHeader(1000, 1000),
                [track]);

        Assert.Equal(2, result.Count);
        Assert.Equal(2u, result[0].SampleNumber);
        Assert.Equal(3u, result[1].SampleNumber);
    }

    [Fact]
    public void Build_ShouldRejectUnsupportedEditPlaybackRate()
    {
        Mp4MediaTrack track = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, 0, 500)
            ],
            editList:
            [
                new Mp4EditListEntry(500, 0, 2, 0)
            ]);

        Assert.Throws<Mp4ParsingException>(
            () => _builder.Build(
                new Mp4MovieHeader(1000, 500),
                [track]));
    }

    [Fact]
    public void Build_ShouldRejectInvalidNegativeEditMediaTime()
    {
        Mp4MediaTrack track = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, 0, 500)
            ],
            editList:
            [
                new Mp4EditListEntry(500, -2, 1, 0)
            ]);

        Assert.Throws<Mp4ParsingException>(
            () => _builder.Build(
                new Mp4MovieHeader(1000, 500),
                [track]));
    }

    [Fact]
    public void Build_ShouldRejectZeroMovieTimescale()
    {
        Mp4MediaTrack track = CreateTrack(
            trackId: 1,
            timescale: 1000,
            locations:
            [
                CreateLocation(1, 0, 500)
            ]);

        Assert.Throws<Mp4ParsingException>(
            () => _builder.Build(
                new Mp4MovieHeader(0, 1000),
                [track]));
    }

    private static Mp4MediaTrack CreateTrack(
        uint trackId,
        uint timescale,
        IEnumerable<Mp4SampleLocation> locations,
        IEnumerable<Mp4EditListEntry>? editList = null)
    {
        Mp4Track track = new(
            trackId,
            0,
            0,
            timescale,
            0,
            Mp4HandlerType.Video,
            [
                new Mp4SampleDescription(1, "avc1")
            ]);

        Mp4Box trackBox = new(
            8,
            "trak",
            0,
            8);

        Mp4SampleTable sampleTable = new(
            Array.Empty<Mp4TimeToSampleEntry>(),
            Array.Empty<Mp4SampleToChunkEntry>(),
            new Mp4SampleSizeTable(1, 0, Array.Empty<uint>()),
            new Mp4ChunkOffsetTable(Array.Empty<ulong>()),
            null);

        Mp4SampleLocationTable locationTable = new(locations);

        Mp4EditList? edits = editList is null
            ? null
            : new Mp4EditList(editList);

        return new Mp4MediaTrack(
            track,
            trackBox,
            sampleTable,
            locationTable,
            edits);
    }

    private static Mp4SampleLocation CreateLocation(
        uint sampleNumber,
        ulong decodeTime,
        uint duration,
        int compositionOffset = 0)
    {
        return new Mp4SampleLocation(
            sampleNumber,
            1,
            0,
            1,
            decodeTime,
            duration,
            true)
        {
            CompositionOffsetTicks = compositionOffset,
            SampleDescriptionIndex = 1
        };
    }
}
