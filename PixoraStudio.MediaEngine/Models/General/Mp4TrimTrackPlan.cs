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

/// <summary>
/// Describes a keyframe-aligned MP4 trim. The effective range can differ
/// from the requested range because this first remuxing stage does not re-encode.
/// </summary>
public sealed class Mp4TrimExportPlan
{
    private readonly Mp4TrimTrackPlan[] _tracks;

    public double RequestedStartSeconds { get; }
    public double RequestedEndSeconds { get; }
    public double EffectiveStartSeconds { get; }
    public double EffectiveEndSeconds { get; }
    public IReadOnlyList<Mp4TrimTrackPlan> Tracks { get; }

    public Mp4TrimExportPlan(
        double requestedStartSeconds,
        double requestedEndSeconds,
        double effectiveStartSeconds,
        double effectiveEndSeconds,
        IEnumerable<Mp4TrimTrackPlan> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        _tracks = tracks.ToArray();

        if (_tracks.Length == 0)
            throw new ArgumentException("An export plan must contain at least one track.", nameof(tracks));

        RequestedStartSeconds = requestedStartSeconds;
        RequestedEndSeconds = requestedEndSeconds;
        EffectiveStartSeconds = effectiveStartSeconds;
        EffectiveEndSeconds = effectiveEndSeconds;
        Tracks = Array.AsReadOnly(_tracks);
    }
}
