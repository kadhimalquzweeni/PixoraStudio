using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4TrackSampleLocationReaderTests
{
    private readonly Mp4TrackSampleLocationReader _reader = new();

    [Fact]
    public void Read_ShouldResolveAllSampleLocations()
    {
        Mp4Track track = CreateTrack();

        Mp4SampleTable sampleTable = CreateSampleTable();

        Mp4SampleLocationTable result =
            _reader.Read(track, sampleTable);

        Assert.Equal(5U, result.SampleCount);

        AssertSample(
            result.GetSample(1),
            1, 1, 1000, 100, 0, 100, true);

        AssertSample(
            result.GetSample(2),
            2, 1, 1100, 200, 100, 100, false);

        AssertSample(
            result.GetSample(3),
            3, 2, 2000, 300, 200, 200, false);

        AssertSample(
            result.GetSample(4),
            4, 2, 2300, 400, 400, 200, true);

        AssertSample(
            result.GetSample(5),
            5, 2, 2700, 500, 600, 200, false);
    }

    [Fact]
    public void Read_ShouldTreatAllSamplesAsSyncWhenSyncTableIsMissing()
    {
        Mp4Track track = CreateTrack();

        Mp4SampleTable sampleTable = CreateSampleTable(
            includeSyncSamples: false);

        Mp4SampleLocationTable result =
            _reader.Read(track, sampleTable);

        for (uint sampleNumber = 1;
             sampleNumber <= result.SampleCount;
             sampleNumber++)
        {
            Assert.True(
                result.GetSample(sampleNumber).IsSyncSample);
        }
    }

    [Fact]
    public void Read_ShouldRejectZeroTimescale()
    {
        Mp4Track track = new(
            trackId: 1,
            width: 1920,
            height: 1080,
            timescale: 0);

        Mp4SampleTable sampleTable = CreateSampleTable();

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(track, sampleTable));
    }

    [Fact]
    public void Read_ShouldRejectNullTrack()
    {
        Mp4SampleTable sampleTable = CreateSampleTable();

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(null!, sampleTable));
    }

    [Fact]
    public void Read_ShouldRejectNullSampleTable()
    {
        Mp4Track track = CreateTrack();

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(track, null!));
    }

    [Fact]
    public void Read_ShouldPreserveEmptySyncSampleTable()
    {
        Mp4Track track = CreateTrack();

        Mp4SampleTable sampleTable = CreateSampleTable(
            syncSamples: new Mp4SyncSampleTable([]));

        Mp4SampleLocationTable result =
            _reader.Read(track, sampleTable);

        for (uint sampleNumber = 1;
             sampleNumber <= result.SampleCount;
             sampleNumber++)
        {
            Assert.False(
                result.GetSample(sampleNumber).IsSyncSample);
        }
    }

    [Fact]
    public void Read_ShouldUseTrackTimescaleForDecodeTimes()
    {
        Mp4Track track = new(
            trackId: 1,
            width: 1920,
            height: 1080,
            timescale: 1000);

        Mp4SampleTable sampleTable = CreateSampleTable();

        Mp4SampleLocationTable result =
            _reader.Read(track, sampleTable);

        Assert.Equal(100UL, result.GetSample(2).DecodeTimeTicks);
        Assert.Equal(200UL, result.GetSample(3).DecodeTimeTicks);
    }

    private static Mp4Track CreateTrack()
    {
        return new Mp4Track(
            trackId: 1,
            width: 1920,
            height: 1080,
            timescale: 1000,
            durationTicks: 800,
            handlerType: Mp4HandlerType.Video);
    }

    private static Mp4SampleTable CreateSampleTable(
        bool includeSyncSamples = true,
        Mp4SyncSampleTable? syncSamples = null)
    {
        Mp4TimeToSampleEntry[] timingEntries =
        [
            new(2, 100),
            new(3, 200)
        ];

        Mp4SampleToChunkEntry[] chunkEntries =
        [
            new(1, 2, 1),
            new(2, 3, 1)
        ];

        Mp4SampleSizeTable sampleSizes = new(
            defaultSampleSize: 0,
            sampleCount: 5,
            sampleSizes: [100, 200, 300, 400, 500]);

        Mp4ChunkOffsetTable chunkOffsets = new(
            [1000UL, 2000UL]);

        Mp4SyncSampleTable? syncTable = syncSamples;

        if (includeSyncSamples && syncTable is null)
        {
            syncTable = new Mp4SyncSampleTable([1, 4]);
        }

        return new Mp4SampleTable(
            timingEntries,
            chunkEntries,
            sampleSizes,
            chunkOffsets,
            syncTable);
    }

    private static void AssertSample(
        Mp4SampleLocation actual,
        uint sampleNumber,
        uint chunkNumber,
        ulong offset,
        uint size,
        ulong decodeTime,
        uint duration,
        bool isSync)
    {
        Assert.Equal(sampleNumber, actual.SampleNumber);
        Assert.Equal(chunkNumber, actual.ChunkNumber);
        Assert.Equal(offset, actual.Offset);
        Assert.Equal(size, actual.Size);
        Assert.Equal(decodeTime, actual.DecodeTimeTicks);
        Assert.Equal(duration, actual.DurationTicks);
        Assert.Equal(isSync, actual.IsSyncSample);
    }

    [Fact]
    public void Read_ShouldIncludeCompositionTimestamps()
    {
        Mp4Track track = new(
            trackId: 1,
            width: 1920,
            height: 1080,
            timescale: 1000,
            durationTicks: 800);

        Mp4SampleTable sampleTable = new(
            [
                new(2, 100),
            new(3, 200)
            ],
            [
                new(1, 2, 1),
            new(2, 3, 1)
            ],
            new Mp4SampleSizeTable(0, 5, [10, 20, 30, 40, 50]),
            new Mp4ChunkOffsetTable([1000UL, 2000UL]),
            null,
            new Mp4CompositionTimeTable(
            [
                new(2, 50),
            new(3, -25)
            ]));

        Mp4TrackSampleLocationReader reader = new();

        Mp4SampleLocationTable result =
            reader.Read(track, sampleTable);

        Assert.Equal(50L, result.GetSample(1).PresentationTimeTicks);
        Assert.Equal(150L, result.GetSample(2).PresentationTimeTicks);
        Assert.Equal(175L, result.GetSample(3).PresentationTimeTicks);
        Assert.Equal(375L, result.GetSample(4).PresentationTimeTicks);
        Assert.Equal(575L, result.GetSample(5).PresentationTimeTicks);
    }

}
