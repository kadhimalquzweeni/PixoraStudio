namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents the sync samples (keyframes) in an MP4 track.
/// Sample numbers are one-based, as defined by the MP4 specification.
/// </summary>
public sealed class Mp4SyncSampleTable
{
    private readonly uint[] _sampleNumbers;

    public uint SampleCount =>
        checked((uint)_sampleNumbers.Length);

    public IReadOnlyList<uint> SampleNumbers { get; }

    public Mp4SyncSampleTable(IEnumerable<uint> sampleNumbers)
    {
        ArgumentNullException.ThrowIfNull(sampleNumbers);

        _sampleNumbers = sampleNumbers.ToArray();

        uint previousSampleNumber = 0;

        foreach (uint sampleNumber in _sampleNumbers)
        {
            if (sampleNumber == 0 ||
                sampleNumber <= previousSampleNumber)
            {
                throw new ArgumentException(
                    "Sync sample numbers must be positive and strictly increasing.",
                    nameof(sampleNumbers));
            }

            previousSampleNumber = sampleNumber;
        }

        SampleNumbers = Array.AsReadOnly(_sampleNumbers);
    }

    /// <summary>
    /// Determines whether a one-based sample number is a sync sample.
    /// </summary>
    public bool IsSyncSample(uint sampleNumber)
    {
        if (sampleNumber == 0)
        {
            return false;
        }

        return Array.BinarySearch(
            _sampleNumbers,
            sampleNumber) >= 0;
    }
}