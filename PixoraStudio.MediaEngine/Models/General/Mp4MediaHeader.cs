namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents timing information from an MP4 media header.
/// </summary>
public sealed class Mp4MediaHeader
{
    public byte Version { get; }

    public uint Timescale { get; }

    public ulong Duration { get; }

    public bool IsDurationUnknown { get; }

    public double? DurationSeconds { get; }

    public Mp4MediaHeader(
        byte version,
        uint timescale,
        ulong duration)
    {
        if (timescale == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timescale),
                "Timescale must be greater than zero.");
        }

        Version = version;
        Timescale = timescale;
        Duration = duration;

        IsDurationUnknown = version switch
        {
            0 => duration == uint.MaxValue,
            1 => duration == ulong.MaxValue,
            _ => false
        };

        DurationSeconds = IsDurationUnknown
            ? null
            : duration / (double)timescale;
    }
}
