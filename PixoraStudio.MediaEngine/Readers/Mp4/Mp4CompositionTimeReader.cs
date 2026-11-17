
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads composition-time information from a ctts box.
/// </summary>
public sealed class Mp4CompositionTimeReader
{
    public IReadOnlyList<Mp4CompositionTimeEntry> Read(
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

        if (box.Type != "ctts")
        {
            throw new Mp4ParsingException(
                "Expected a ctts box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The ctts box extends beyond the stream.");
            }

            if (box.DataSize < 8)
            {
                throw new Mp4ParsingException(
                    "The ctts box is too small.");
            }

            stream.Position = box.DataOffset;

            byte[] header = new byte[8];
            stream.ReadExactly(header);

            uint versionAndFlags =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(0, 4));

            uint version = versionAndFlags >> 24;

            if (version > 1)
            {
                throw new Mp4ParsingException(
                    "Unsupported ctts version.");
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
                    "The ctts entry count does not match its box size.");
            }

            List<Mp4CompositionTimeEntry> entries = [];

            byte[] entryData = new byte[entrySize];

            for (uint i = 0; i < entryCount; i++)
            {
                stream.ReadExactly(entryData);

                uint sampleCount =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        entryData.AsSpan(0, 4));

                uint rawOffset =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        entryData.AsSpan(4, 4));

                if (sampleCount == 0)
                {
                    throw new Mp4ParsingException(
                        "A ctts entry has a zero sample count.");
                }

                int sampleOffset;

                if (version == 0)
                {
                    if (rawOffset > int.MaxValue)
                    {
                        throw new Mp4ParsingException(
                            "A version 0 ctts offset exceeds the supported range.");
                    }

                    sampleOffset = (int)rawOffset;
                }
                else
                {
                    sampleOffset = unchecked((int)rawOffset);
                }

                entries.Add(new Mp4CompositionTimeEntry(
                    sampleCount,
                    sampleOffset));
            }

            return entries;
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading ctts.", ex);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                "The ctts entry count is too large.", ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
