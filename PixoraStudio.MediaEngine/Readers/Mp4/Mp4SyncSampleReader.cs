using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads sync sample numbers from an MP4 stss box.
/// </summary>
public sealed class Mp4SyncSampleReader
{
    private const uint MaximumSampleCount = 10_000_000;

    public Mp4SyncSampleTable Read(
        Stream stream,
        Mp4Box box)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(box);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        if (box.Type != "stss")
        {
            throw new Mp4ParsingException(
                "Expected an stss box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The stss box extends beyond the stream.");
            }

            if (box.DataSize < 8)
            {
                throw new Mp4ParsingException(
                    "The stss box is too small.");
            }

            stream.Position = box.DataOffset;

            byte[] header = new byte[8];
            stream.ReadExactly(header);

            uint versionAndFlags =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(0, 4));

            if ((versionAndFlags >> 24) != 0)
            {
                throw new Mp4ParsingException(
                    "Unsupported stss version.");
            }

            uint entryCount =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(4, 4));

            if (entryCount > MaximumSampleCount)
            {
                throw new Mp4ParsingException(
                    "The stss sample count exceeds the supported limit.");
            }

            long expectedDataSize =
                8L + ((long)entryCount * 4L);

            if (expectedDataSize != box.DataSize)
            {
                throw new Mp4ParsingException(
                    "The stss sample count does not match its box size.");
            }

            List<uint> sampleNumbers = new(
                checked((int)entryCount));

            byte[] sampleData = new byte[4];

            uint previousSampleNumber = 0;

            for (uint i = 0; i < entryCount; i++)
            {
                stream.ReadExactly(sampleData);

                uint sampleNumber =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        sampleData);

                if (sampleNumber == 0 ||
                    sampleNumber <= previousSampleNumber)
                {
                    throw new Mp4ParsingException(
                        "The stss sample numbers must be positive and strictly increasing.");
                }

                sampleNumbers.Add(sampleNumber);

                previousSampleNumber = sampleNumber;
            }

            return new Mp4SyncSampleTable(sampleNumbers);
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading stss.",
                ex);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                "The stss sample count is too large.",
                ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}