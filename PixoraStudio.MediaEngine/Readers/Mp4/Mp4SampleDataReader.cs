
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads encoded sample data from MP4 media-data regions.
/// </summary>
public sealed class Mp4SampleDataReader
{
    /// <summary>
    /// Reads the encoded bytes for a sample.
    /// </summary>
    public byte[] Read(
        Stream stream,
        Mp4SampleLocation sample,
        IReadOnlyList<Mp4MediaData> mediaDataRegions)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(mediaDataRegions);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new Mp4ParsingException(
                "The stream must support reading and seeking.");
        }

        Mp4MediaData? containingRegion = mediaDataRegions
            .FirstOrDefault(region =>
                region.Contains(sample.Offset, sample.Size));

        if (containingRegion is null)
        {
            throw new Mp4ParsingException(
                "The sample is outside the available mdat regions.");
        }

        if (sample.Offset > long.MaxValue)
        {
            throw new Mp4ParsingException(
                "The sample offset exceeds the supported stream range.");
        }

        long sampleOffset = (long)sample.Offset;

        if (sampleOffset > stream.Length ||
            sample.Size > stream.Length - sampleOffset)
        {
            throw new Mp4ParsingException(
                "The sample extends beyond the end of the stream.");
        }

        long originalPosition = stream.Position;

        try
        {
            stream.Position = sampleOffset;

            byte[] buffer = new byte[checked((int)sample.Size)];

            int bytesRead = 0;

            while (bytesRead < buffer.Length)
            {
                int count = stream.Read(
                    buffer,
                    bytesRead,
                    buffer.Length - bytesRead);

                if (count == 0)
                {
                    throw new Mp4ParsingException(
                        "Unexpected end of stream while reading sample data.");
                }

                bytesRead += count;
            }

            return buffer;
        }
        catch (IOException exception)
        {
            throw new Mp4ParsingException(
                "Failed to read sample data.",
                exception);
        }
        catch (OverflowException exception)
        {
            throw new Mp4ParsingException(
                "The sample size exceeds the supported range.",
                exception);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
