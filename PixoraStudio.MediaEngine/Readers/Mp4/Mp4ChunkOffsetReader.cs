using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads chunk offsets from MP4 stco and co64 boxes.
/// </summary>
public sealed class Mp4ChunkOffsetReader
{
    private const uint MaximumChunkCount = 10_000_000;

    public Mp4ChunkOffsetTable Read(
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

        bool is32Bit = box.Type == "stco";
        bool is64Bit = box.Type == "co64";

        if (!is32Bit && !is64Bit)
        {
            throw new Mp4ParsingException(
                "Expected an stco or co64 box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The chunk offset box extends beyond the stream.");
            }

            if (box.DataSize < 8)
            {
                throw new Mp4ParsingException(
                    "The chunk offset box is too small.");
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
                    "Unsupported chunk offset box version.");
            }

            uint entryCount =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(4, 4));

            if (entryCount > MaximumChunkCount)
            {
                throw new Mp4ParsingException(
                    "The chunk count exceeds the supported limit.");
            }

            int offsetSize = is32Bit ? 4 : 8;

            long expectedDataSize =
                8L + ((long)entryCount * offsetSize);

            if (expectedDataSize != box.DataSize)
            {
                throw new Mp4ParsingException(
                    "The chunk count does not match the box size.");
            }

            List<ulong> offsets = new(
                checked((int)entryCount));

            byte[] offsetData = new byte[offsetSize];

            for (uint i = 0; i < entryCount; i++)
            {
                stream.ReadExactly(offsetData);

                ulong offset = is32Bit
                    ? BinaryPrimitives.ReadUInt32BigEndian(
                        offsetData)
                    : BinaryPrimitives.ReadUInt64BigEndian(
                        offsetData);

                offsets.Add(offset);
            }

            return new Mp4ChunkOffsetTable(offsets);
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading chunk offsets.",
                ex);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                "The chunk offset count is too large.",
                ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}