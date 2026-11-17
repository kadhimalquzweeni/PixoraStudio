namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Contains the resolved locations of all samples in an MP4 track.
/// </summary>
public sealed class Mp4SampleLocationTable
{
    private readonly Mp4SampleLocation[] _samples;

    public uint SampleCount =>
        checked((uint)_samples.Length);

    public IReadOnlyList<Mp4SampleLocation> Samples { get; }

    public Mp4SampleLocationTable(
        IEnumerable<Mp4SampleLocation> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        _samples = samples.ToArray();

        Samples = Array.AsReadOnly(_samples);
    }

    /// <summary>
    /// Gets a sample using its one-based sample number.
    /// </summary>
    public Mp4SampleLocation GetSample(uint sampleNumber)
    {
        if (sampleNumber == 0 ||
            sampleNumber > SampleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleNumber));
        }

        return _samples[checked((int)sampleNumber - 1)];
    }
}