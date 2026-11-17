using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads sample descriptions belonging to an MP4 track.
/// </summary>
public sealed class Mp4TrackSampleDescriptionReader
{
    private readonly Mp4SampleDescriptionReader _sampleDescriptionReader = new();

    private readonly Mp4VideoSampleEntryReader _videoSampleEntryReader = new();

    private readonly Mp4AudioSampleEntryReader _audioSampleEntryReader = new();

    public IReadOnlyList<Mp4SampleDescription> Read(
        Stream stream,
        Mp4Box trackBox,
        Mp4HandlerType handlerType)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(trackBox);

        if (trackBox.Type != "trak")
        {
            throw new Mp4ParsingException(
                "Expected a trak box.");
        }

        Mp4Box? mdia = GetOptionalChild(trackBox, "mdia");

        if (mdia is null)
            return Array.Empty<Mp4SampleDescription>();

        Mp4Box? minf = GetOptionalChild(mdia, "minf");

        if (minf is null)
            return Array.Empty<Mp4SampleDescription>();

        Mp4Box? stbl = GetOptionalChild(minf, "stbl");

        if (stbl is null)
            return Array.Empty<Mp4SampleDescription>();

        Mp4Box? stsd = GetOptionalChild(stbl, "stsd");

        if (stsd is null)
            return Array.Empty<Mp4SampleDescription>();

        IReadOnlyList<Mp4Box> entries =
            _sampleDescriptionReader.Read(stream, stsd);

        List<Mp4SampleDescription> descriptions = [];

        for (int i = 0; i < entries.Count; i++)
        {
            Mp4Box entry = entries[i];

            Mp4VideoSampleEntry? video = null;
            Mp4AudioSampleEntry? audio = null;

            if (handlerType == Mp4HandlerType.Video)
            {
                video = _videoSampleEntryReader.Read(
                    stream,
                    entry);
            }
            else if (handlerType == Mp4HandlerType.Audio &&
                     entry.Type == "mp4a")
            {
                audio = _audioSampleEntryReader.Read(
                    stream,
                    entry);
            }

            descriptions.Add(
                new Mp4SampleDescription(
                    checked((uint)i + 1),
                    entry.Type,
                    video,
                    audio));
        }

        return descriptions.AsReadOnly();
    }

    private static Mp4Box? GetOptionalChild(
        Mp4Box parent,
        string type)
    {
        Mp4Box[] matches = parent.Children
            .Where(box => box.Type == type)
            .ToArray();

        if (matches.Length > 1)
        {
            throw new Mp4ParsingException(
                $"The '{parent.Type}' box contains multiple '{type}' boxes.");
        }

        return matches.SingleOrDefault();
    }
}