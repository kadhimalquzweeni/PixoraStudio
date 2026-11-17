using PixoraStudio.MediaEngine.Containers.Mp4;

namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents a fully resolved MP4 media track.
/// </summary>
public sealed class Mp4MediaTrack
{
    public Mp4Track Track { get; }

    public Mp4Box TrackBox { get; }

    public Mp4SampleTable SampleTable { get; }

    public Mp4SampleLocationTable SampleLocations { get; }

    public Mp4EditList? EditList { get; }

    public uint TrackId => Track.TrackId;

    public Mp4HandlerType HandlerType => Track.HandlerType;

    public uint Timescale => Track.Timescale;

    public uint SampleCount => SampleLocations.SampleCount;

    public Mp4MediaTrack(
        Mp4Track track,
        Mp4Box trackBox,
        Mp4SampleTable sampleTable,
        Mp4SampleLocationTable sampleLocations,
        Mp4EditList? editList = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(trackBox);
        ArgumentNullException.ThrowIfNull(sampleTable);
        ArgumentNullException.ThrowIfNull(sampleLocations);

        Track = track;
        TrackBox = trackBox;
        SampleTable = sampleTable;
        SampleLocations = sampleLocations;
        EditList = editList;
    }

    /// <summary>
    /// Reads encoded samples in their original decode order.
    /// </summary>
    public IEnumerable<Mp4EncodedSample> ReadSamples(
        Stream stream,
        IReadOnlyList<Mp4MediaData> mediaDataRegions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mediaDataRegions);

        return new Mp4TrackSampleEnumerator().Enumerate(
            stream,
            SampleLocations,
            mediaDataRegions,
            cancellationToken);
    }
}