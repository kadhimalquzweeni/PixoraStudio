
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads encoded samples belonging to an MP4 track.
/// </summary>
public sealed class Mp4TrackSampleReader
{
    private readonly Mp4SampleDataReader _sampleDataReader = new();

    /// <summary>
    /// Reads an individual sample using its one-based sample number.
    /// </summary>
    public Mp4EncodedSample Read(
        Stream stream,
        Mp4SampleLocationTable locations,
        uint sampleNumber,
        IReadOnlyList<Mp4MediaData> mediaDataRegions)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(mediaDataRegions);

        Mp4SampleLocation location;

        try
        {
            location = locations.GetSample(sampleNumber);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new Mp4ParsingException(
                $"Sample number {sampleNumber} does not exist.",
                exception);
        }

        byte[] data = _sampleDataReader.Read(
            stream,
            location,
            mediaDataRegions);

        return new Mp4EncodedSample(location, data);
    }
}
