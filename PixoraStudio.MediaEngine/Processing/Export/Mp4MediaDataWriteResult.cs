namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Describes the media payload written to an MP4 mdat box.
/// </summary>
public sealed class Mp4MediaDataWriteResult
{
    private readonly Dictionary<uint, IReadOnlyList<ulong>> _chunkOffsets;

    public ulong BoxOffset { get; }
    public ulong DataOffset { get; }
    public ulong DataSize { get; }
    public IReadOnlyDictionary<uint, IReadOnlyList<ulong>> ChunkOffsetsByTrack => _chunkOffsets;

    public Mp4MediaDataWriteResult(
        ulong boxOffset,
        ulong dataOffset,
        ulong dataSize,
        IDictionary<uint, IReadOnlyList<ulong>> chunkOffsetsByTrack)
    {
        ArgumentNullException.ThrowIfNull(chunkOffsetsByTrack);
        BoxOffset = boxOffset;
        DataOffset = dataOffset;
        DataSize = dataSize;
        _chunkOffsets = new Dictionary<uint, IReadOnlyList<ulong>>(chunkOffsetsByTrack);
    }
}
