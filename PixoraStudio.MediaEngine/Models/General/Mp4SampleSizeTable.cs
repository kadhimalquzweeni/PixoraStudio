namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents the sample sizes contained in an MP4 track.
/// </summary>
public sealed class Mp4SampleSizeTable
{
    private readonly uint[] _sampleSizes;

    public uint DefaultSampleSize { get; }

    public uint SampleCount { get; }

    public IReadOnlyList<uint> SampleSizes => _sampleSizes;

    public Mp4SampleSizeTable(
        uint defaultSampleSize,
        uint sampleCount,
        IEnumerable<uint> sampleSizes)
    {
        ArgumentNullException.ThrowIfNull(sampleSizes);

        DefaultSampleSize = defaultSampleSize;
        SampleCount = sampleCount;

        _sampleSizes = sampleSizes.ToArray();

        if (defaultSampleSize == 0 &&
            _sampleSizes.Length != sampleCount)
        {
            throw new ArgumentException(
                "The number of sample sizes must match sample count.");
        }

        if (defaultSampleSize != 0 &&
            _sampleSizes.Length != 0)
        {
            throw new ArgumentException(
                "Individual sample sizes must be empty when a default size is used.");
        }
    }

    public uint GetSampleSize(uint sampleIndex)
    {
        if (sampleIndex >= SampleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleIndex));
        }

        return DefaultSampleSize != 0
            ? DefaultSampleSize
            : _sampleSizes[checked((int)sampleIndex)];
    }
}
