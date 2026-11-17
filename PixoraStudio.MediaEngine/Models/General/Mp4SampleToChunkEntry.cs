namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Describes how samples are grouped into MP4 chunks.
/// </summary>
public sealed record Mp4SampleToChunkEntry(
    uint FirstChunk,
    uint SamplesPerChunk,
    uint SampleDescriptionIndex);
