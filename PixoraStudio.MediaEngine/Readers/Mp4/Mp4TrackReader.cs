using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Combines MP4 track metadata and sample descriptions.
/// </summary>
public sealed class Mp4TrackReader
{
    private readonly Mp4TrackHeaderReader _trackHeaderReader = new();
    private readonly Mp4MediaHeaderReader _mediaHeaderReader = new();
    private readonly Mp4HandlerReader _handlerReader = new();
    private readonly Mp4TrackSampleDescriptionReader _sampleDescriptionReader = new();

    public IReadOnlyList<Mp4Track> Read(
        Stream stream,
        IReadOnlyList<Mp4Box> topLevelBoxes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(topLevelBoxes);

        Mp4Box? moov = topLevelBoxes.FirstOrDefault(
            box => box.Type == "moov");

        if (moov is null)
        {
            throw new Mp4ParsingException(
                "The MP4 file does not contain a moov box.");
        }

        List<Mp4Track> tracks = [];

        foreach (Mp4Box trak in moov.Children.Where(
            box => box.Type == "trak"))
        {
            Mp4Track? track = ReadTrack(stream, trak);

            if (track is not null)
            {
                tracks.Add(track);
            }
        }

        return tracks;
    }

    private Mp4Track? ReadTrack(
        Stream stream,
        Mp4Box trak)
    {
        Mp4Box? tkhd = trak.Children.FirstOrDefault(
            box => box.Type == "tkhd");

        Mp4Box? mdia = trak.Children.FirstOrDefault(
            box => box.Type == "mdia");

        if (tkhd is null || mdia is null)
        {
            throw new Mp4ParsingException(
                "A track is missing its tkhd or mdia box.");
        }

        Mp4Box? mdhd = mdia.Children.FirstOrDefault(
            box => box.Type == "mdhd");

        Mp4Box? hdlr = mdia.Children.FirstOrDefault(
            box => box.Type == "hdlr");

        if (mdhd is null || hdlr is null)
        {
            throw new Mp4ParsingException(
                "A track is missing its mdhd or hdlr box.");
        }

        Mp4Track header = _trackHeaderReader.Read(
            stream,
            tkhd);

        var timing = _mediaHeaderReader.Read(
            stream,
            mdhd);

        Mp4HandlerType handlerType = _handlerReader.Read(
            stream,
            hdlr);

        IReadOnlyList<Mp4SampleDescription> descriptions =
            _sampleDescriptionReader.Read(
                stream,
                trak,
                handlerType);

        return new Mp4Track(
            header.TrackId,
            header.Width,
            header.Height,
            timing.Timescale,
            timing.DurationTicks,
            handlerType,
            descriptions);
    }
}
