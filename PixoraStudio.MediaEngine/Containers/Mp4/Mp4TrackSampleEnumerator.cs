
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Enumerates encoded samples belonging to an MP4 track.
/// </summary>
public sealed class Mp4TrackSampleEnumerator
{
    private readonly Mp4TrackSampleReader _sampleReader = new();

    /// <summary>
    /// Enumerates all encoded samples in their original track order.
    /// </summary>
    public IEnumerable<Mp4EncodedSample> Enumerate(
        Stream stream,
        Mp4SampleLocationTable locations,
        IReadOnlyList<Mp4MediaData> mediaDataRegions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(mediaDataRegions);

        return EnumerateSamples(
            stream,
            locations,
            mediaDataRegions,
            1,
            locations.SampleCount,
            cancellationToken);
    }

    /// <summary>
    /// Enumerates a selected range of encoded samples.
    /// Sample numbers are one-based.
    /// </summary>
    public IEnumerable<Mp4EncodedSample> Enumerate(
        Stream stream,
        Mp4SampleLocationTable locations,
        uint startSampleNumber,
        uint sampleCount,
        IReadOnlyList<Mp4MediaData> mediaDataRegions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(mediaDataRegions);

        if (startSampleNumber == 0)
        {
            throw new Mp4ParsingException(
                "The starting sample number must be greater than zero.");
        }

        if (sampleCount == 0)
        {
            throw new Mp4ParsingException(
                "The requested sample count must be greater than zero.");
        }

        if (startSampleNumber > locations.SampleCount)
        {
            throw new Mp4ParsingException(
                "The starting sample number exceeds the track sample count.");
        }

        uint remainingSamples =
            locations.SampleCount - startSampleNumber + 1;

        if (sampleCount > remainingSamples)
        {
            throw new Mp4ParsingException(
                "The requested sample range exceeds the track sample count.");
        }

        return EnumerateSamples(
            stream,
            locations,
            mediaDataRegions,
            startSampleNumber,
            sampleCount,
            cancellationToken);
    }

    private IEnumerable<Mp4EncodedSample> EnumerateSamples(
        Stream stream,
        Mp4SampleLocationTable locations,
        IReadOnlyList<Mp4MediaData> mediaDataRegions,
        uint startSampleNumber,
        uint sampleCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        for (uint offset = 0; offset < sampleCount; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint sampleNumber = checked(startSampleNumber + offset);

            yield return _sampleReader.Read(
                stream,
                locations,
                sampleNumber,
                mediaDataRegions);
        }
    }
}
