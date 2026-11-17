
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads the sample tables associated with an MP4 track.
/// </summary>
public sealed class Mp4TrackSampleTableReader
{
    private readonly Mp4SampleTableLocator _sampleTableLocator = new();
    private readonly Mp4SampleTableReader _sampleTableReader = new();

    /// <summary>
    /// Reads the sample tables belonging to the specified track.
    /// </summary>
    public Mp4SampleTable Read(Stream stream, Mp4Box trackBox)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(trackBox);

        if (trackBox.Type != "trak")
        {
            throw new Mp4ParsingException(
                "Expected a trak box.");
        }

        Mp4Box sampleTableBox = _sampleTableLocator.Locate(trackBox);

        return _sampleTableReader.Read(stream, sampleTableBox);
    }
}
