using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Coordinates the readers required to retrieve encoded samples
/// from an MP4 track.
/// </summary>
public sealed class Mp4TrackSamplePipelineReader
{
    private readonly Mp4TrackSampleTableReader _sampleTableReader = new();
    private readonly Mp4TrackSampleLocationReader _sampleLocationReader = new();
    private readonly Mp4MediaDataReader _mediaDataReader = new();
    private readonly Mp4TrackSampleEnumerator _sampleEnumerator = new();

    /// <summary>
    /// Reads all encoded samples belonging to an MP4 track.
    /// </summary>
    public IEnumerable<Mp4EncodedSample> Read(
        Stream stream,
        Mp4Box root,
        Mp4Box trackBox,
        Mp4Track track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(trackBox);
        ArgumentNullException.ThrowIfNull(track);

        Mp4SampleTable sampleTable =
            _sampleTableReader.Read(stream, trackBox);

        Mp4SampleLocationTable sampleLocations =
            _sampleLocationReader.Read(track, sampleTable);

        IReadOnlyList<Mp4MediaData> mediaDataRegions =
            _mediaDataReader.Read(root);

        return _sampleEnumerator.Enumerate(
            stream,
            sampleLocations,
            mediaDataRegions,
            cancellationToken);
    }
}
