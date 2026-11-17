
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads decoding-time information from an stts box.
/// </summary>
public sealed class Mp4TimeToSampleReader
{
    public IReadOnlyList<Mp4TimeToSampleEntry> Read(
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

        if (box.Type != "stts")
        {
            throw new Mp4ParsingException(
                "Expected an stts box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The stts box extends beyond the stream.");
            }

            if (box.DataSize < 8)
            {
                throw new Mp4ParsingException(
                    "The stts box is too small.");
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
                    "Unsupported stts version.");
            }

            uint entryCount =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(4, 4));

            const int entrySize = 8;

            long expectedSize = checked(
                8L + (long)entryCount * entrySize);

            if (expectedSize != box.DataSize)
            {
                throw new Mp4ParsingException(
                    "The stts entry count does not match its box size.");
            }

            List<Mp4TimeToSampleEntry> entries = [];

            byte[] entryData = new byte[entrySize];

            for (uint i = 0; i < entryCount; i++)
            {
                stream.ReadExactly(entryData);

                uint sampleCount =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        entryData.AsSpan(0, 4));

                uint sampleDelta =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        entryData.AsSpan(4, 4));

                if (sampleCount == 0)
                {
                    throw new Mp4ParsingException(
                        "An stts entry has a zero sample count.");
                }

                entries.Add(new Mp4TimeToSampleEntry(
                    sampleCount,
                    sampleDelta));
            }

            return entries;
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading stts.", ex);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                "The stts entry count is too large.", ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
