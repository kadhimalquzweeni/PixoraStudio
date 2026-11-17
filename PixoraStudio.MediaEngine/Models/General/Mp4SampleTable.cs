using PixoraStudio.MediaEngine.Containers.Mp4;

namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Contains the sample tables associated with an MP4 track.
/// </summary>
public sealed class Mp4SampleTable
{
    public IReadOnlyList<Mp4TimeToSampleEntry> TimeToSampleEntries { get; }

    public IReadOnlyList<Mp4SampleToChunkEntry> SampleToChunkEntries { get; }

    public Mp4SampleSizeTable SampleSizes { get; }

    public Mp4ChunkOffsetTable ChunkOffsets { get; }

    public Mp4SyncSampleTable? SyncSamples { get; }

    public Mp4CompositionTimeTable? CompositionTimes { get; }

    public Mp4SampleTable(
        IEnumerable<Mp4TimeToSampleEntry> timeToSampleEntries,
        IEnumerable<Mp4SampleToChunkEntry> sampleToChunkEntries,
        Mp4SampleSizeTable sampleSizes,
        Mp4ChunkOffsetTable chunkOffsets,
        Mp4SyncSampleTable? syncSamples,
        Mp4CompositionTimeTable? compositionTimes = null)
    {
        ArgumentNullException.ThrowIfNull(timeToSampleEntries);
        ArgumentNullException.ThrowIfNull(sampleToChunkEntries);
        ArgumentNullException.ThrowIfNull(sampleSizes);
        ArgumentNullException.ThrowIfNull(chunkOffsets);

        TimeToSampleEntries =
            Array.AsReadOnly(timeToSampleEntries.ToArray());

        SampleToChunkEntries =
            Array.AsReadOnly(sampleToChunkEntries.ToArray());

        SampleSizes = sampleSizes;
        ChunkOffsets = chunkOffsets;
        SyncSamples = syncSamples;
        CompositionTimes = compositionTimes;
    }
}
