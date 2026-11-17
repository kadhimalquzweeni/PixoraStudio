
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4SampleLocationResolverTests
{
    private readonly Mp4SampleLocationResolver _resolver = new();

    [Fact]
    public void Resolve_ShouldCalculateSampleLocationsAcrossMultipleChunks()
    {
        Mp4SampleLocationTable table = ResolveStandardTrack();

        Assert.Equal(5u, table.SampleCount);

        Assert.Equal(1000UL, table.GetSample(1).Offset);
        Assert.Equal(1010UL, table.GetSample(2).Offset);
        Assert.Equal(2000UL, table.GetSample(3).Offset);
        Assert.Equal(2030UL, table.GetSample(4).Offset);
        Assert.Equal(2070UL, table.GetSample(5).Offset);
    }

    [Fact]
    public void Resolve_ShouldCalculateSampleSizes()
    {
        Mp4SampleLocationTable table = ResolveStandardTrack();

        Assert.Equal(10u, table.GetSample(1).Size);
        Assert.Equal(20u, table.GetSample(2).Size);
        Assert.Equal(30u, table.GetSample(3).Size);
        Assert.Equal(40u, table.GetSample(4).Size);
        Assert.Equal(50u, table.GetSample(5).Size);
    }

    [Fact]
    public void Resolve_ShouldCalculateChunkNumbers()
    {
        Mp4SampleLocationTable table = ResolveStandardTrack();

        Assert.Equal(1u, table.GetSample(1).ChunkNumber);
        Assert.Equal(1u, table.GetSample(2).ChunkNumber);
        Assert.Equal(2u, table.GetSample(3).ChunkNumber);
        Assert.Equal(2u, table.GetSample(4).ChunkNumber);
        Assert.Equal(2u, table.GetSample(5).ChunkNumber);
    }

    [Fact]
    public void Resolve_ShouldCalculateDecodeTimestamps()
    {
        Mp4SampleLocationTable table = ResolveStandardTrack();

        Assert.Equal(0UL, table.GetSample(1).DecodeTimeTicks);
        Assert.Equal(100UL, table.GetSample(2).DecodeTimeTicks);
        Assert.Equal(200UL, table.GetSample(3).DecodeTimeTicks);
        Assert.Equal(400UL, table.GetSample(4).DecodeTimeTicks);
        Assert.Equal(600UL, table.GetSample(5).DecodeTimeTicks);
    }

    [Fact]
    public void Resolve_ShouldCalculateSampleDurations()
    {
        Mp4SampleLocationTable table = ResolveStandardTrack();

        Assert.Equal(100u, table.GetSample(1).DurationTicks);
        Assert.Equal(100u, table.GetSample(2).DurationTicks);
        Assert.Equal(200u, table.GetSample(3).DurationTicks);
        Assert.Equal(200u, table.GetSample(4).DurationTicks);
        Assert.Equal(200u, table.GetSample(5).DurationTicks);
    }

    [Fact]
    public void Resolve_ShouldIdentifySyncSamples()
    {
        Mp4SampleLocationTable table = ResolveStandardTrack();

        Assert.True(table.GetSample(1).IsSyncSample);
        Assert.False(table.GetSample(2).IsSyncSample);
        Assert.False(table.GetSample(3).IsSyncSample);
        Assert.True(table.GetSample(4).IsSyncSample);
        Assert.False(table.GetSample(5).IsSyncSample);
    }

    [Fact]
    public void Resolve_ShouldTreatAllSamplesAsSyncWhenTableIsAbsent()
    {
        Mp4SampleLocationTable table = _resolver.Resolve(
            CreateSampleSizes(),
            CreateSampleToChunkEntries(),
            CreateChunkOffsets(),
            CreateTimeToSampleEntries(),
            1000);

        Assert.All(
            table.Samples,
            sample => Assert.True(sample.IsSyncSample));
    }

    [Fact]
    public void Resolve_ShouldTreatNoSamplesAsSyncWhenTableIsEmpty()
    {
        Mp4SampleLocationTable table = _resolver.Resolve(
            CreateSampleSizes(),
            CreateSampleToChunkEntries(),
            CreateChunkOffsets(),
            CreateTimeToSampleEntries(),
            1000,
            new Mp4SyncSampleTable([]));

        Assert.All(
            table.Samples,
            sample => Assert.False(sample.IsSyncSample));
    }

    [Fact]
    public void Resolve_ShouldRejectZeroTimescale()
    {
        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                CreateSampleToChunkEntries(),
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                0));
    }

    [Fact]
    public void Resolve_ShouldRejectMismatchedTimingSampleCount()
    {
        Mp4TimeToSampleEntry[] incorrectTiming =
        [
            new(2, 100),
            new(2, 200)
        ];

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                CreateSampleToChunkEntries(),
                CreateChunkOffsets(),
                incorrectTiming,
                1000));
    }

    [Fact]
    public void Resolve_ShouldRejectMissingChunkOffsets()
    {
        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                CreateSampleToChunkEntries(),
                new Mp4ChunkOffsetTable([]),
                CreateTimeToSampleEntries(),
                1000));
    }

    [Fact]
    public void Resolve_ShouldRejectInsufficientChunkMapping()
    {
        Mp4SampleToChunkEntry[] incorrectMapping =
        [
            new(1, 1, 1)
        ];

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                incorrectMapping,
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                1000));
    }

    [Fact]
    public void Resolve_ShouldRejectSyncSampleOutsideTrack()
    {
        Mp4SyncSampleTable incorrectSyncSamples =
            new([1, 6]);

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                CreateSampleToChunkEntries(),
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                1000,
                incorrectSyncSamples));
    }

    [Fact]
    public void Resolve_ShouldRejectMissingSampleToChunkEntries()
    {
        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                [],
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                1000));
    }

    [Fact]
    public void SampleLocationTable_ShouldRejectOutOfRangeSampleNumber()
    {
        Mp4SampleLocationTable table = ResolveStandardTrack();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.GetSample(0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.GetSample(6));
    }

    private Mp4SampleLocationTable ResolveStandardTrack()
    {
        return _resolver.Resolve(
            CreateSampleSizes(),
            CreateSampleToChunkEntries(),
            CreateChunkOffsets(),
            CreateTimeToSampleEntries(),
            1000,
            new Mp4SyncSampleTable([1, 4]));
    }

    private static Mp4SampleSizeTable CreateSampleSizes()
    {
        return new Mp4SampleSizeTable(
            0,
            5,
            [10, 20, 30, 40, 50]);
    }

    private static Mp4SampleToChunkEntry[]
        CreateSampleToChunkEntries()
    {
        return
        [
            new(1, 2, 1),
            new(2, 3, 1)
        ];
    }

    private static Mp4ChunkOffsetTable CreateChunkOffsets()
    {
        return new Mp4ChunkOffsetTable(
            [1000UL, 2000UL]);
    }

    private static Mp4TimeToSampleEntry[]
        CreateTimeToSampleEntries()
    {
        return
        [
            new(2, 100),
            new(3, 200)
        ];
    }
    [Fact]
    public void Resolve_ShouldRejectChunkMappingBeyondAvailableChunks()
    {
        Mp4SampleToChunkEntry[] invalidMapping =
        [
            new(1, 2, 1),
        new(3, 1, 1)
        ];

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                invalidMapping,
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                1000));
    }

    [Fact]
    public void Resolve_ShouldRejectSampleOffsetOverflow()
    {
        Mp4SampleSizeTable sizes =
            new(0, 1, [10]);

        Mp4ChunkOffsetTable offsets =
            new([ulong.MaxValue - 5]);

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                sizes,
                [new(1, 1, 1)],
                offsets,
                [new(1, 100)],
                1000));
    }

    [Fact]
    public void Resolve_ShouldCalculateLargeDecodeTimestamps()
    {
        Mp4SampleSizeTable sizes =
            new(0, 2, [1, 1]);

        Mp4ChunkOffsetTable offsets =
            new([100UL]);

        Mp4TimeToSampleEntry[] timing =
        [
            new(2, uint.MaxValue)
        ];

        Mp4SampleLocationTable result = _resolver.Resolve(
            sizes,
            [new(1, 2, 1)],
            offsets,
            timing,
            1000);

        Assert.Equal(
            0UL,
            result.GetSample(1).DecodeTimeTicks);

        Assert.Equal(
            (ulong)uint.MaxValue,
            result.GetSample(2).DecodeTimeTicks);
    }

    [Fact]
    public void Resolve_ShouldCalculatePresentationTimestamps()
    {
        Mp4CompositionTimeTable compositionTimes =
            new(
            [
                new(2, 50),
                new(3, -25)
            ]);

        Mp4SampleLocationTable table = _resolver.Resolve(
            CreateSampleSizes(),
            CreateSampleToChunkEntries(),
            CreateChunkOffsets(),
            CreateTimeToSampleEntries(),
            1000,
            new Mp4SyncSampleTable([1, 4]),
            compositionTimes);

        Assert.Equal(50L, table.GetSample(1).PresentationTimeTicks);
        Assert.Equal(150L, table.GetSample(2).PresentationTimeTicks);
        Assert.Equal(175L, table.GetSample(3).PresentationTimeTicks);
        Assert.Equal(375L, table.GetSample(4).PresentationTimeTicks);
        Assert.Equal(575L, table.GetSample(5).PresentationTimeTicks);
    }

    [Fact]
    public void Resolve_ShouldPreserveCompositionOffsets()
    {
        Mp4CompositionTimeTable compositionTimes =
            new(
            [
                new(2, 50),
                new(3, -25)
            ]);

        Mp4SampleLocationTable table = _resolver.Resolve(
            CreateSampleSizes(),
            CreateSampleToChunkEntries(),
            CreateChunkOffsets(),
            CreateTimeToSampleEntries(),
            1000,
            null,
            compositionTimes);

        Assert.Equal(50, table.GetSample(1).CompositionOffsetTicks);
        Assert.Equal(50, table.GetSample(2).CompositionOffsetTicks);
        Assert.Equal(-25, table.GetSample(3).CompositionOffsetTicks);
        Assert.Equal(-25, table.GetSample(4).CompositionOffsetTicks);
        Assert.Equal(-25, table.GetSample(5).CompositionOffsetTicks);
    }

    [Fact]
    public void Resolve_ShouldUseDecodeTimestampWhenCompositionTableIsAbsent()
    {
        Mp4SampleLocationTable table = ResolveStandardTrack();

        foreach (Mp4SampleLocation sample in table.Samples)
        {
            Assert.Equal(
                (long)sample.DecodeTimeTicks,
                sample.PresentationTimeTicks);

            Assert.Equal(0, sample.CompositionOffsetTicks);
        }
    }

    [Fact]
    public void Resolve_ShouldRejectMismatchedCompositionSampleCount()
    {
        Mp4CompositionTimeTable compositionTimes =
            new(
            [
                new(2, 50),
                new(2, -25)
            ]);

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                CreateSampleToChunkEntries(),
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                1000,
                null,
                compositionTimes));
    }

    [Fact]
    public void Resolve_ShouldRejectZeroCompositionSampleCount()
    {
        Mp4CompositionTimeTable compositionTimes =
            new(
            [
                new(1, 50),
                new(0, 100),
                new(4, -25)
            ]);

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                CreateSampleToChunkEntries(),
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                1000,
                null,
                compositionTimes));
    }

    [Fact]
    public void Resolve_ShouldPreserveSampleDescriptionIndexes()
    {
        Mp4SampleToChunkEntry[] mappings =
        [
            new(1, 2, 1),
        new(2, 3, 2)
        ];

        Mp4SampleLocationTable result = _resolver.Resolve(
            CreateSampleSizes(),
            mappings,
            CreateChunkOffsets(),
            CreateTimeToSampleEntries(),
            1000);

        Assert.Equal(1u, result.GetSample(1).SampleDescriptionIndex);
        Assert.Equal(1u, result.GetSample(2).SampleDescriptionIndex);
        Assert.Equal(2u, result.GetSample(3).SampleDescriptionIndex);
        Assert.Equal(2u, result.GetSample(4).SampleDescriptionIndex);
        Assert.Equal(2u, result.GetSample(5).SampleDescriptionIndex);
    }

    [Fact]
    public void Resolve_ShouldPreserveSampleDescriptionIndexAcrossAllChunks()
    {
        Mp4SampleToChunkEntry[] sampleToChunkEntries =
        [
            new(1, 2, 1),
        new(2, 3, 2)
        ];

        Mp4SampleLocationTable result = _resolver.Resolve(
            CreateSampleSizes(),
            sampleToChunkEntries,
            CreateChunkOffsets(),
            CreateTimeToSampleEntries(),
            1000);

        Assert.Collection(
            result.Samples,
            sample => Assert.Equal(1u, sample.SampleDescriptionIndex),
            sample => Assert.Equal(1u, sample.SampleDescriptionIndex),
            sample => Assert.Equal(2u, sample.SampleDescriptionIndex),
            sample => Assert.Equal(2u, sample.SampleDescriptionIndex),
            sample => Assert.Equal(2u, sample.SampleDescriptionIndex));
    }

    [Fact]
    public void Resolve_ShouldRejectZeroSampleDescriptionIndex()
    {
        Mp4SampleToChunkEntry[] mappings =
        [
            new(1, 2, 0),
        new(2, 3, 1)
        ];

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                mappings,
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                1000));
    }

    [Fact]
    public void Resolve_ShouldPreserveDifferentSampleDescriptionIndexesAcrossChunks()
    {
        Mp4SampleToChunkEntry[] mappings =
        [
            new(1, 2, 1),
        new(2, 3, 2)
        ];

        Mp4SampleLocationTable result = _resolver.Resolve(
            CreateSampleSizes(),
            mappings,
            CreateChunkOffsets(),
            CreateTimeToSampleEntries(),
            1000);

        Assert.Collection(
            result.Samples,
            sample => Assert.Equal(1u, sample.SampleDescriptionIndex),
            sample => Assert.Equal(1u, sample.SampleDescriptionIndex),
            sample => Assert.Equal(2u, sample.SampleDescriptionIndex),
            sample => Assert.Equal(2u, sample.SampleDescriptionIndex),
            sample => Assert.Equal(2u, sample.SampleDescriptionIndex));
    }

    [Fact]
    public void Resolve_ShouldPreserveCompositionOffsetsWhenEntriesChange()
    {
        Mp4CompositionTimeTable compositionTimes = new(
        [
            new(1, 100),
        new(1, 200),
        new(1, -100),
        new(2, 50)
        ]);

        Mp4SampleLocationTable result = _resolver.Resolve(
            CreateSampleSizes(),
            CreateSampleToChunkEntries(),
            CreateChunkOffsets(),
            CreateTimeToSampleEntries(),
            1000,
            compositionTimes: compositionTimes);

        Assert.Equal(100, result.GetSample(1).CompositionOffsetTicks);
        Assert.Equal(200, result.GetSample(2).CompositionOffsetTicks);
        Assert.Equal(-100, result.GetSample(3).CompositionOffsetTicks);
        Assert.Equal(50, result.GetSample(4).CompositionOffsetTicks);
        Assert.Equal(50, result.GetSample(5).CompositionOffsetTicks);
    }

    [Fact]
    public void Resolve_ShouldRejectChunkMappingWithFirstChunkBeyondAvailableChunks()
    {
        Mp4SampleToChunkEntry[] mappings =
        [
            new(1, 2, 1),
        new(3, 1, 1)
        ];

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                mappings,
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                1000));
    }

    [Fact]
    public void Resolve_ShouldRejectChunkTableThatDescribesTooManySamples()
    {
        Mp4SampleToChunkEntry[] mappings =
        [
            new(1, 3, 1),
        new(2, 3, 1)
        ];

        Assert.Throws<Mp4ParsingException>(
            () => _resolver.Resolve(
                CreateSampleSizes(),
                mappings,
                CreateChunkOffsets(),
                CreateTimeToSampleEntries(),
                1000));
    }

}
