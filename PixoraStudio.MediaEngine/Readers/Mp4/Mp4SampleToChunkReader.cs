
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads sample-to-chunk information from an stsc box.
/// </summary>
public sealed class Mp4SampleToChunkReader
{
    public IReadOnlyList<Mp4SampleToChunkEntry> Read(
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

        if (box.Type != "stsc")
        {
            throw new Mp4ParsingException(
                "Expected an stsc box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The stsc box extends beyond the stream.");
            }

            if (box.DataSize < 8)
            {
                throw new Mp4ParsingException(
                    "The stsc box is too small.");
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
                    "Unsupported stsc version.");
            }

            uint entryCount =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(4, 4));

            const int entrySize = 12;

            long expectedSize = checked(
                8L + (long)entryCount * entrySize);

            if (expectedSize != box.DataSize)
            {
                throw new Mp4ParsingException(
                    "The stsc entry count does not match its box size.");
            }

            List<Mp4SampleToChunkEntry> entries = [];

            byte[] entryData = new byte[entrySize];

            uint previousFirstChunk = 0;

            for (uint i = 0; i < entryCount; i++)
            {
                stream.ReadExactly(entryData);

                uint firstChunk =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        entryData.AsSpan(0, 4));

                uint samplesPerChunk =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        entryData.AsSpan(4, 4));

                uint sampleDescriptionIndex =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        entryData.AsSpan(8, 4));

                if (firstChunk == 0 ||
                    firstChunk <= previousFirstChunk)
                {
                    throw new Mp4ParsingException(
                        "Invalid stsc first chunk ordering.");
                }

                if (samplesPerChunk == 0 ||
                    sampleDescriptionIndex == 0)
                {
                    throw new Mp4ParsingException(
                        "Invalid stsc entry values.");
                }

                entries.Add(new Mp4SampleToChunkEntry(
                    firstChunk,
                    samplesPerChunk,
                    sampleDescriptionIndex));

                previousFirstChunk = firstChunk;
            }

            if (entries.Count > 0 &&
                entries[0].FirstChunk != 1)
            {
                throw new Mp4ParsingException(
                    "The first stsc entry must start at chunk 1.");
            }

            return entries;
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading stsc.", ex);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                "The stsc entry count is too large.", ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
