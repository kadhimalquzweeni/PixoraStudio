namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// A decode-order sample selection for one track in a trim export.
/// Sample numbers are one-based and remain contiguous.
/// </summary>
public sealed class Mp4TrimTrackPlan
{
    private readonly Mp4SampleLocation[] _samples;

    public uint TrackId { get; }
    public Mp4HandlerType HandlerType { get; }
    public IReadOnlyList<Mp4SampleLocation> Samples { get; }
    public uint FirstSampleNumber => _samples[0].SampleNumber;
    public uint LastSampleNumber => _samples[^1].SampleNumber;

    public Mp4TrimTrackPlan(
        uint trackId,
        Mp4HandlerType handlerType,
        IEnumerable<Mp4SampleLocation> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _samples = samples.ToArray();

        if (_samples.Length == 0)
            throw new ArgumentException("A trim track plan must contain at least one sample.", nameof(samples));

        for (int i = 1; i < _samples.Length; i++)
        {
            if (_samples[i].SampleNumber != _samples[i - 1].SampleNumber + 1)
                throw new ArgumentException("Trim track samples must be contiguous and in decode order.", nameof(samples));
        }

        TrackId = trackId;
        HandlerType = handlerType;
        Samples = Array.AsReadOnly(_samples);
    }
}

