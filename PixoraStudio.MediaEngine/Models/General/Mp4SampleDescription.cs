namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents one sample description belonging to an MP4 track.
/// </summary>
public sealed class Mp4SampleDescription
{
    public uint Index { get; }

    public string Format { get; }

    public Mp4VideoSampleEntry? Video { get; }

    public Mp4AudioSampleEntry? Audio { get; }

    public Mp4SampleDescription(
        uint index,
        string format,
        Mp4VideoSampleEntry? video = null,
        Mp4AudioSampleEntry? audio = null)
    {
        if (index == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        ArgumentNullException.ThrowIfNull(format);

        Index = index;
        Format = format;
        Video = video;
        Audio = audio;
    }
}