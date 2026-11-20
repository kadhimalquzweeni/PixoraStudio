namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Describes the requested and keyframe-aligned effective range for a managed MP4 trim.
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
