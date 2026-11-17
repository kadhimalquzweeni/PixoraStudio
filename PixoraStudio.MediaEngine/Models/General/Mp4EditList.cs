namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents the edit list associated with an MP4 track.
/// </summary>
public sealed class Mp4EditList
{
    private readonly Mp4EditListEntry[] _entries;

    public IReadOnlyList<Mp4EditListEntry> Entries { get; }

    public uint EntryCount =>
        checked((uint)_entries.Length);

    public Mp4EditList(IEnumerable<Mp4EditListEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = entries.ToArray();
        Entries = Array.AsReadOnly(_entries);
    }
}