using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Resolves the individual sample locations belonging to an MP4 track.
/// </summary>
public sealed class Mp4TrackSampleLocationReader
{
    private readonly Mp4SampleLocationResolver _resolver = new();

    /// <summary>
    /// Resolves the sample locations for the specified track.
    /// </summary>
    public Mp4SampleLocationTable Read(
        Mp4Track track,
        Mp4SampleTable sampleTable)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(sampleTable);

        if (track.Timescale == 0)
        {
            throw new Mp4ParsingException(
                "The track timescale cannot be zero.");
        }

        return _resolver.Resolve(
            sampleTable.SampleSizes,
            sampleTable.SampleToChunkEntries.ToArray(),
            sampleTable.ChunkOffsets,
            sampleTable.TimeToSampleEntries.ToArray(),
            track.Timescale,
            sampleTable.SyncSamples,
            sampleTable.CompositionTimes);
    }
}
