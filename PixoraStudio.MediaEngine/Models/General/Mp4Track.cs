namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents metadata extracted from an MP4 track.
/// </summary>
public sealed class Mp4Track
{
    public uint TrackId { get; }

    public Mp4HandlerType HandlerType { get; }

    public double Width { get; }

    public double Height { get; }

    public uint Timescale { get; }

    public ulong DurationTicks { get; }

    public IReadOnlyList<Mp4SampleDescription> SampleDescriptions { get; }

    public double DurationSeconds =>
        Timescale == 0
            ? 0
            : (double)DurationTicks / Timescale;

    public Mp4Track(
        uint trackId,
        double width,
        double height,
        uint timescale = 0,
        ulong durationTicks = 0,
        Mp4HandlerType handlerType = Mp4HandlerType.Unknown,
        IEnumerable<Mp4SampleDescription>? sampleDescriptions = null)
    {
        TrackId = trackId;
        Width = width;
        Height = height;
        Timescale = timescale;
        DurationTicks = durationTicks;
        HandlerType = handlerType;

        SampleDescriptions = Array.AsReadOnly(
            sampleDescriptions?.ToArray() ?? []);
    }
}
