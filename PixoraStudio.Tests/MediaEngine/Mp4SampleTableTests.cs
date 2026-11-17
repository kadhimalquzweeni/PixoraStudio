
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4SampleTableTests
{
    [Fact]
    public void Constructor_ShouldStoreSampleTables()
    {
        Mp4TimeToSampleEntry[] timing =
        [
            new(2, 100),
            new(3, 200)
        ];

        Mp4SampleToChunkEntry[] chunkMapping =
        [
            new(1, 2, 1),
            new(2, 3, 1)
        ];

        Mp4SampleSizeTable sizes =
            new(0, 2, [100, 200]);

        Mp4ChunkOffsetTable offsets =
            new([1000UL, 2000UL]);

        Mp4SyncSampleTable syncSamples =
            new([1U]);

        Mp4SampleTable result = new(
            timing,
            chunkMapping,
            sizes,
            offsets,
            syncSamples);

        Assert.Equal(timing, result.TimeToSampleEntries);
        Assert.Equal(chunkMapping, result.SampleToChunkEntries);
        Assert.Same(sizes, result.SampleSizes);
        Assert.Same(offsets, result.ChunkOffsets);
        Assert.Same(syncSamples, result.SyncSamples);
    }

    [Fact]
    public void Constructor_ShouldAllowMissingSyncSampleTable()
    {
        Mp4SampleTable result = CreateSampleTable();

        Assert.Null(result.SyncSamples);
    }

    [Fact]
    public void Constructor_ShouldAllowEmptySyncSampleTable()
    {
        Mp4SampleTable result = CreateSampleTable(
            new Mp4SyncSampleTable([]));

        Assert.NotNull(result.SyncSamples);
        Assert.Empty(result.SyncSamples.SampleNumbers);
    }

    [Fact]
    public void Constructor_ShouldCopyTimingEntries()
    {
        Mp4TimeToSampleEntry[] timing =
        [
            new(2, 100)
        ];

        Mp4SampleTable result = new(
            timing,
            [new(1, 2, 1)],
            new Mp4SampleSizeTable(100, 2, []),
            new Mp4ChunkOffsetTable([1000UL]),
            null);

        timing[0] = new(5, 500);

        Assert.Equal(
            new Mp4TimeToSampleEntry(2, 100),
            result.TimeToSampleEntries[0]);
    }

    [Fact]
    public void Constructor_ShouldRejectNullTimingEntries()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4SampleTable(
                null!,
                [],
                new Mp4SampleSizeTable(100, 2, []),
                new Mp4ChunkOffsetTable([1000UL]),
                null));
    }

    [Fact]
    public void Constructor_ShouldRejectNullChunkMappingEntries()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4SampleTable(
                [],
                null!,
                new Mp4SampleSizeTable(100, 2, []),
                new Mp4ChunkOffsetTable([1000UL]),
                null));
    }

    [Fact]
    public void Constructor_ShouldRejectNullSampleSizes()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4SampleTable(
                [],
                [],
                null!,
                new Mp4ChunkOffsetTable([1000UL]),
                null));
    }

    [Fact]
    public void Constructor_ShouldRejectNullChunkOffsets()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4SampleTable(
                [],
                [],
                new Mp4SampleSizeTable(100, 2, []),
                null!,
                null));
    }

    private static Mp4SampleTable CreateSampleTable(
        Mp4SyncSampleTable? syncSamples = null)
    {
        return new Mp4SampleTable(
            [new(2, 100)],
            [new(1, 2, 1)],
            new Mp4SampleSizeTable(100, 2, []),
            new Mp4ChunkOffsetTable([1000UL]),
            syncSamples);
    }

    [Fact]
    public void Constructor_ShouldStoreCompositionTimeTable()
    {
        Mp4CompositionTimeTable compositionTimes =
            new(
            [
                new(2, 50),
            new(3, -25)
            ]);

        Mp4SampleTable result = new(
            [new(5, 100)],
            [new(1, 5, 1)],
            new Mp4SampleSizeTable(100, 5, []),
            new Mp4ChunkOffsetTable([1000UL]),
            null,
            compositionTimes);

        Assert.Same(compositionTimes, result.CompositionTimes);
    }

    [Fact]
    public void Constructor_ShouldAllowMissingCompositionTimeTable()
    {
        Mp4SampleTable result = CreateSampleTable();

        Assert.Null(result.CompositionTimes);
    }

}
