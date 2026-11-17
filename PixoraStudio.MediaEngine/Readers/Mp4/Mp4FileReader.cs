using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Timeline;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads and validates a complete non-fragmented MP4 file.
/// </summary>
public sealed class Mp4FileReader
{
    private readonly Mp4Reader _boxReader = new();
    private readonly Mp4TrackReader _trackReader = new();
    private readonly Mp4SampleTableReader _sampleTableReader = new();
    private readonly Mp4TrackSampleLocationReader _sampleLocationReader = new();
    private readonly Mp4MediaDataReader _mediaDataReader = new();

    private readonly Mp4MovieHeaderReader _movieHeaderReader = new();
    private readonly Mp4EditListReader _editListReader = new();
    private readonly Mp4EditListLocator _editListLocator = new();

    private readonly Mp4PresentationTimelineBuilder _timelineBuilder = new();

    public Mp4MediaInfo Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return Read(stream);
    }

    /// <summary>
    /// Reads an MP4 from an existing seekable stream.
    /// The caller retains ownership of the stream.
    /// </summary>
    public Mp4MediaInfo Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        long originalPosition = stream.Position;

        try
        {
            IReadOnlyList<Mp4Box> topLevelBoxes =
                _boxReader.ReadBoxTree(stream);

            Mp4Box movieBox = GetRequiredSingleBox(
                topLevelBoxes,
                "moov");

            Mp4Box movieHeaderBox = GetRequiredSingleBox(
                movieBox.Children,
                "mvhd");

            Mp4MovieHeader movieHeader =
                _movieHeaderReader.Read(
                    stream,
                    movieHeaderBox);

            Mp4Box[] trackBoxes = movieBox.Children
                .Where(box => box.Type == "trak")
                .ToArray();

            if (trackBoxes.Length == 0)
            {
                throw new Mp4ParsingException(
                    "The MP4 file does not contain any tracks.");
            }

            IReadOnlyList<Mp4Track> tracks =
                _trackReader.Read(stream, topLevelBoxes);

            if (tracks.Count != trackBoxes.Length)
            {
                throw new Mp4ParsingException(
                    "The parsed track count does not match the movie track count.");
            }

            // Construct a lightweight root so the existing mdat reader
            // can inspect all top-level boxes and their descendants.
            Mp4Box root = new(
                checked((ulong)stream.Length),
                "root",
                0,
                8);

            foreach (Mp4Box box in topLevelBoxes)
            {
                root.AddChild(box);
            }

            IReadOnlyList<Mp4MediaData> mediaDataRegions =
                _mediaDataReader.Read(root);

            if (mediaDataRegions.Count == 0)
            {
                throw new Mp4ParsingException(
                    "The MP4 file does not contain an mdat box.");
            }

            List<Mp4MediaTrack> mediaTracks = [];

            for (int i = 0; i < trackBoxes.Length; i++)
            {
                Mp4Box trackBox = trackBoxes[i];
                Mp4Track track = tracks[i];

                Mp4SampleTable sampleTable =
                    _sampleTableReader.Read(
                        stream,
                        trackBox);

                Mp4SampleLocationTable locations =
                    _sampleLocationReader.Read(
                        track,
                        sampleTable);

                ValidateSampleDescriptions(
                    track,
                    locations);

                ValidateSampleData(
                    locations,
                    mediaDataRegions);

                Mp4Box? editListBox =
                    _editListLocator.Find(trackBox);

                Mp4EditList? editList =
                    editListBox is null
                        ? null
                        : _editListReader.Read(
                            stream,
                            editListBox);

                mediaTracks.Add(
                    new Mp4MediaTrack(
                        track,
                        trackBox,
                        sampleTable,
                        locations,
                        editList));
            }

            IReadOnlyList<Mp4PresentationSample> timeline =
                _timelineBuilder.Build(
                    movieHeader,
                    mediaTracks);

            return new Mp4MediaInfo(
                movieHeader,
                mediaTracks,
                mediaDataRegions,
                timeline);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                "The MP4 file contains values outside the supported range.",
                ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static Mp4Box GetRequiredSingleBox(
        IReadOnlyList<Mp4Box> boxes,
        string type)
    {
        Mp4Box[] matches = boxes
            .Where(box => box.Type == type)
            .ToArray();

        if (matches.Length != 1)
        {
            throw new Mp4ParsingException(
                $"Expected exactly one '{type}' box.");
        }

        return matches[0];
    }

    private static void ValidateSampleDescriptions(
        Mp4Track track,
        Mp4SampleLocationTable locations)
    {
        foreach (Mp4SampleLocation location in locations.Samples)
        {
            if (location.SampleDescriptionIndex == 0 ||
                location.SampleDescriptionIndex >
                    track.SampleDescriptions.Count)
            {
                throw new Mp4ParsingException(
                    $"Sample {location.SampleNumber} in track {track.TrackId} " +
                    "references an invalid sample description.");
            }
        }
    }

    private static void ValidateSampleData(
        Mp4SampleLocationTable locations,
        IReadOnlyList<Mp4MediaData> mediaDataRegions)
    {
        foreach (Mp4SampleLocation location in locations.Samples)
        {
            bool contained = mediaDataRegions.Any(
                region => region.Contains(
                    location.Offset,
                    location.Size));

            if (!contained)
            {
                throw new Mp4ParsingException(
                    $"Sample {location.SampleNumber} is outside every mdat region.");
            }
        }
    }
}