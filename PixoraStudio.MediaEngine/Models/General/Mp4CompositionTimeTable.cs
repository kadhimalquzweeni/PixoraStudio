namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Contains composition-time information for all samples in an MP4 track.
/// </summary>
public sealed class Mp4CompositionTimeTable
{
    private readonly Mp4CompositionTimeEntry[] _entries;

    public uint EntryCount =>
        checked((uint)_entries.Length);

    public IReadOnlyList<Mp4CompositionTimeEntry> Entries { get; }

    public Mp4CompositionTimeTable(
        IEnumerable<Mp4CompositionTimeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = entries.ToArray();

        Entries = Array.AsReadOnly(_entries);
    }

    /// <summary>
    /// Gets the composition offset for a one-based sample number.
    /// </summary>
    public int GetCompositionOffset(uint sampleNumber)
    {
        if (sampleNumber == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleNumber));
        }

        uint currentSample = 0;

        foreach (Mp4CompositionTimeEntry entry in _entries)
        {
            uint nextSample = checked(
                currentSample + entry.SampleCount);

            if (sampleNumber <= nextSample)
            {
                return entry.SampleOffset;
            }

            currentSample = nextSample;
        }

        throw new ArgumentOutOfRangeException(
            nameof(sampleNumber));
    }
}
