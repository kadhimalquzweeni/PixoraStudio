namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents the chunk offsets contained in an MP4 track.
/// </summary>
public sealed class Mp4ChunkOffsetTable
{
    private readonly ulong[] _offsets;

    public uint ChunkCount => checked((uint)_offsets.Length);

    public IReadOnlyList<ulong> Offsets { get; }

    public Mp4ChunkOffsetTable(IEnumerable<ulong> offsets)
    {
        ArgumentNullException.ThrowIfNull(offsets);

        _offsets = offsets.ToArray();
        Offsets = Array.AsReadOnly(_offsets);
    }

    /// <summary>
    /// Gets a chunk offset using a zero-based chunk index.
    /// </summary>
    public ulong GetOffset(uint chunkIndex)
    {
        if (chunkIndex >= ChunkCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkIndex));
        }

        return _offsets[checked((int)chunkIndex)];
    }
}