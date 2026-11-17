
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Resolves individual MP4 sample locations using the track's sample tables.
/// </summary>
public sealed class Mp4SampleLocationResolver
{
    private const uint MaximumSampleCount = 10_000_000;

    public Mp4SampleLocationTable Resolve(
        Mp4SampleSizeTable sampleSizes,
        Mp4SampleToChunkEntry[] sampleToChunkEntries,
        Mp4ChunkOffsetTable chunkOffsets,
        Mp4TimeToSampleEntry[] timeToSampleEntries,
        uint timescale,
        Mp4SyncSampleTable? syncSamples = null,
        Mp4CompositionTimeTable? compositionTimes = null)
    {
        ArgumentNullException.ThrowIfNull(sampleSizes);
        ArgumentNullException.ThrowIfNull(sampleToChunkEntries);
        ArgumentNullException.ThrowIfNull(chunkOffsets);
        ArgumentNullException.ThrowIfNull(timeToSampleEntries);

        if (timescale == 0)
        {
            throw new Mp4ParsingException(
                "The track timescale must be greater than zero.");
        }

        uint sampleCount = sampleSizes.SampleCount;

        if (sampleCount > MaximumSampleCount)
        {
            throw new Mp4ParsingException(
                "The sample count exceeds the supported limit.");
        }

        if (sampleCount == 0)
        {
            if (chunkOffsets.ChunkCount != 0 ||
                sampleToChunkEntries.Length != 0 ||
                timeToSampleEntries.Length != 0 ||
                (syncSamples?.SampleCount ?? 0) != 0 ||
                (compositionTimes?.Entries.Count ?? 0) != 0)
            {
                throw new Mp4ParsingException(
                    "Empty sample tables contain inconsistent entries.");
            }

            return new Mp4SampleLocationTable([]);
        }

        ValidateSampleToChunkEntries(
            sampleToChunkEntries,
            chunkOffsets.ChunkCount);

        ValidateTimeToSampleEntries(
            timeToSampleEntries,
            sampleCount);

        ValidateSyncSamples(
            syncSamples,
            sampleCount);

        ValidateCompositionTimeEntries(
            compositionTimes,
            sampleCount);

        if (chunkOffsets.ChunkCount == 0)
        {
            throw new Mp4ParsingException(
                "The track contains samples but has no chunk offsets.");
        }

        if (sampleToChunkEntries.Length == 0)
        {
            throw new Mp4ParsingException(
                "The track has no sample-to-chunk entries.");
        }

        List<Mp4SampleLocation> locations = new(
            checked((int)sampleCount));

        int timeEntryIndex = 0;
        uint remainingTimeSamples =
            timeToSampleEntries[0].SampleCount;

        int compositionEntryIndex = 0;
        uint remainingCompositionSamples =
            compositionTimes?.Entries.Count > 0
                ? compositionTimes.Entries[0].SampleCount
                : 0;

        ulong currentDecodeTime = 0;
        uint currentSampleNumber = 1;

        for (uint chunkIndex = 1;
             chunkIndex <= chunkOffsets.ChunkCount;
             chunkIndex++)
        {
            Mp4SampleToChunkEntry mapping =
                GetMappingForChunk(
                    sampleToChunkEntries,
                    chunkIndex);

            ulong currentOffset =
                chunkOffsets.GetOffset(chunkIndex - 1);

            for (uint i = 0;
                 i < mapping.SamplesPerChunk;
                 i++)
            {
                if (currentSampleNumber > sampleCount)
                {
                    throw new Mp4ParsingException(
                        "The chunk table describes more samples than the sample size table.");
                }

                if (remainingTimeSamples == 0)
                {
                    throw new Mp4ParsingException(
                        "The timing table does not contain enough sample durations.");
                }

                uint sampleSize =
                    sampleSizes.GetSampleSize(
                        currentSampleNumber - 1);

                uint duration =
                    timeToSampleEntries[timeEntryIndex].SampleDelta;

                bool isSyncSample = syncSamples is null ||
                    syncSamples.IsSyncSample(currentSampleNumber);

                int compositionOffset = 0;

                if (compositionTimes is not null)
                {
                    if (remainingCompositionSamples == 0)
                    {
                        throw new Mp4ParsingException(
                            "The composition time table does not contain enough sample offsets.");
                    }

                    compositionOffset =
                        compositionTimes.Entries[
                            compositionEntryIndex].SampleOffset;

                    remainingCompositionSamples--;

                    if (remainingCompositionSamples == 0 &&
                        currentSampleNumber < sampleCount)
                    {
                        compositionEntryIndex++;

                        remainingCompositionSamples =
                            compositionTimes.Entries[
                                compositionEntryIndex].SampleCount;
                    }
                }

                Mp4SampleLocation location =
                    new(
                        currentSampleNumber,
                        chunkIndex,
                        currentOffset,
                        sampleSize,
                        currentDecodeTime,
                        duration,
                        isSyncSample)
                    {
                        CompositionOffsetTicks = compositionOffset,
                        SampleDescriptionIndex = mapping.SampleDescriptionIndex
                    };

                try
                {
                    // Ensure the presentation timestamp is representable.
                    _ = location.PresentationTimeTicks;
                }
                catch (OverflowException ex)
                {
                    throw new Mp4ParsingException(
                        "The sample presentation timestamp exceeds the supported range.",
                        ex);
                }

                locations.Add(location);

                currentOffset = AddChecked(
                    currentOffset,
                    sampleSize,
                    "The sample byte offset exceeds the supported range.");

                currentDecodeTime = AddChecked(
                    currentDecodeTime,
                    duration,
                    "The sample decode timestamp exceeds the supported range.");

                currentSampleNumber++;
                remainingTimeSamples--;

                if (remainingTimeSamples == 0 &&
                    currentSampleNumber <= sampleCount)
                {
                    timeEntryIndex++;

                    if (timeEntryIndex >= timeToSampleEntries.Length)
                    {
                        throw new Mp4ParsingException(
                            "The timing table does not contain enough entries.");
                    }

                    remainingTimeSamples =
                        timeToSampleEntries[timeEntryIndex].SampleCount;
                }
            }
        }

        if (currentSampleNumber != sampleCount + 1)
        {
            throw new Mp4ParsingException(
                "The chunk table does not describe all samples.");
        }

        return new Mp4SampleLocationTable(locations);
    }

    private static Mp4SampleToChunkEntry GetMappingForChunk(
        Mp4SampleToChunkEntry[] entries,
        uint chunkNumber)
    {
        Mp4SampleToChunkEntry? selected = null;

        foreach (Mp4SampleToChunkEntry entry in entries)
        {
            if (entry.FirstChunk > chunkNumber)
            {
                break;
            }

            selected = entry;
        }

        return selected ??
            throw new Mp4ParsingException(
                "No sample-to-chunk mapping exists for a chunk.");
    }

    private static void ValidateSampleToChunkEntries(
        Mp4SampleToChunkEntry[] entries,
        uint chunkCount)
    {
        uint previousFirstChunk = 0;

        foreach (Mp4SampleToChunkEntry entry in entries)
        {
            if (entry.FirstChunk == 0 ||
                entry.FirstChunk <= previousFirstChunk ||
                entry.FirstChunk > chunkCount ||
                entry.SamplesPerChunk == 0 ||
                entry.SampleDescriptionIndex == 0)
            {
                throw new Mp4ParsingException(
                    "The sample-to-chunk table contains invalid entries.");
            }

            previousFirstChunk = entry.FirstChunk;
        }

        if (entries.Length == 0 || entries[0].FirstChunk != 1)
        {
            throw new Mp4ParsingException(
                "The first sample-to-chunk entry must start at chunk 1.");
        }
    }

    private static void ValidateTimeToSampleEntries(
        Mp4TimeToSampleEntry[] entries,
        uint sampleCount)
    {
        ulong totalSamples = 0;

        foreach (Mp4TimeToSampleEntry entry in entries)
        {
            if (entry.SampleCount == 0 ||
                entry.SampleDelta == 0)
            {
                throw new Mp4ParsingException(
                    "The timing table contains invalid entries.");
            }

            totalSamples += entry.SampleCount;
        }

        if (totalSamples != sampleCount)
        {
            throw new Mp4ParsingException(
                "The timing table sample count does not match the sample size table.");
        }
    }

    private static void ValidateSyncSamples(
        Mp4SyncSampleTable? syncSamples,
        uint sampleCount)
    {
        if (syncSamples is null)
        {
            return;
        }

        if (syncSamples.SampleNumbers.Any(
            sampleNumber => sampleNumber > sampleCount))
        {
            throw new Mp4ParsingException(
                "The sync sample table references a sample outside the track.");
        }
    }

    private static void ValidateCompositionTimeEntries(
        Mp4CompositionTimeTable? compositionTimes,
        uint sampleCount)
    {
        if (compositionTimes is null)
        {
            return;
        }

        ulong totalSamples = 0;

        foreach (Mp4CompositionTimeEntry entry in compositionTimes.Entries)
        {
            if (entry.SampleCount == 0)
            {
                throw new Mp4ParsingException(
                    "The composition time table contains a zero sample count.");
            }

            totalSamples += entry.SampleCount;
        }

        if (totalSamples != sampleCount)
        {
            throw new Mp4ParsingException(
                "The composition time table sample count does not match the sample size table.");
        }
    }

    private static ulong AddChecked(
        ulong left,
        ulong right,
        string errorMessage)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                errorMessage,
                ex);
        }
    }
}
