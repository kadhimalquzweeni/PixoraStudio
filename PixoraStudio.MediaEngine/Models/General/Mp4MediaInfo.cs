using PixoraStudio.MediaEngine.Containers.Mp4;

namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Contains the resolved metadata and playback timeline of an MP4 file.
/// </summary>
public sealed class Mp4MediaInfo
{
    private readonly Mp4MediaTrack[] _tracks;
    private readonly Mp4MediaData[] _mediaDataRegions;
    private readonly Mp4PresentationSample[] _presentationSamples;

    public Mp4MovieHeader MovieHeader { get; }

    public IReadOnlyList<Mp4MediaTrack> Tracks { get; }

    public IReadOnlyList<Mp4MediaData> MediaDataRegions { get; }

    public IReadOnlyList<Mp4PresentationSample> PresentationSamples { get; }

    public double DurationSeconds =>
        MovieHeader.DurationSeconds;

    public Mp4MediaInfo(
        Mp4MovieHeader movieHeader,
        IEnumerable<Mp4MediaTrack> tracks,
        IEnumerable<Mp4MediaData> mediaDataRegions,
        IEnumerable<Mp4PresentationSample> presentationSamples)
    {
        ArgumentNullException.ThrowIfNull(movieHeader);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(mediaDataRegions);
        ArgumentNullException.ThrowIfNull(presentationSamples);

        MovieHeader = movieHeader;

        _tracks = tracks.ToArray();
        _mediaDataRegions = mediaDataRegions.ToArray();
        _presentationSamples = presentationSamples.ToArray();

        Tracks = Array.AsReadOnly(_tracks);
        MediaDataRegions = Array.AsReadOnly(_mediaDataRegions);
        PresentationSamples = Array.AsReadOnly(_presentationSamples);
    }

    public Mp4MediaTrack? GetTrack(uint trackId)
    {
        return _tracks.FirstOrDefault(
            track => track.TrackId == trackId);
    }

    public IReadOnlyList<Mp4MediaTrack> GetTracks(
        Mp4HandlerType handlerType)
    {
        return _tracks
            .Where(track => track.HandlerType == handlerType)
            .ToArray();
    }

    /// <summary>
    /// Reads samples in presentation order.
    /// The stream must remain open for the duration of enumeration.
    /// </summary>
    public IEnumerable<Mp4PlaybackSample> ReadPresentationSamples(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        return ReadPresentationSamplesCore(
            stream,
            cancellationToken);
    }

    private IEnumerable<Mp4PlaybackSample> ReadPresentationSamplesCore(
        Stream stream,
        CancellationToken cancellationToken)
    {
        long originalPosition = stream.Position;

        try
        {
            foreach (Mp4PresentationSample presentationSample
                     in _presentationSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Mp4MediaTrack track =
                    GetTrack(presentationSample.TrackId)
                    ?? throw new InvalidOperationException(
                        "The presentation sample references an unknown track.");

                byte[] data = ReadSampleData(
                    stream,
                    presentationSample.Location);

                Mp4EncodedSample encodedSample = new(
                    presentationSample.Location,
                    data);

                yield return new Mp4PlaybackSample(
                    track.TrackId,
                    track.HandlerType,
                    presentationSample.PresentationTimeSeconds,
                    encodedSample);
            }
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private byte[] ReadSampleData(
        Stream stream,
        Mp4SampleLocation location)
    {
        bool contained = _mediaDataRegions.Any(
            region => region.Contains(
                location.Offset,
                location.Size));

        if (!contained)
        {
            throw new InvalidOperationException(
                $"Sample {location.SampleNumber} is outside every mdat region.");
        }

        if (location.Size > int.MaxValue)
        {
            throw new InvalidOperationException(
                "The sample exceeds the supported in-memory size.");
        }

        ulong streamLength = checked((ulong)stream.Length);

        if (location.Offset > long.MaxValue ||
            location.Offset > streamLength ||
            location.Size > streamLength - location.Offset)
        {
            throw new InvalidOperationException(
                "The sample extends beyond the stream.");
        }

        byte[] data = new byte[checked((int)location.Size)];

        stream.Position = checked((long)location.Offset);
        stream.ReadExactly(data);

        return data;
    }
}