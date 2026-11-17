namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Contains the movie-level timing information from an MP4 mvhd box.
/// </summary>
public sealed record Mp4MovieHeader(
    uint Timescale,
    ulong DurationTicks)
{
    public double DurationSeconds =>
        Timescale == 0
            ? 0
            : (double)DurationTicks / Timescale;
}