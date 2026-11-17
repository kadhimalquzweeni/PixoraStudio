using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads movie timing information from an MP4 mvhd box.
/// </summary>
public sealed class Mp4MovieHeaderReader
{
    public Mp4MovieHeader Read(Stream stream, Mp4Box box)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(box);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        if (box.Type != "mvhd")
        {
            throw new Mp4ParsingException(
                "Expected an mvhd box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The mvhd box extends beyond the stream.");
            }

            if (box.DataSize < 1)
            {
                throw new Mp4ParsingException(
                    "The mvhd box does not contain a version.");
            }

            stream.Position = box.DataOffset;

            int version = stream.ReadByte();

            int requiredSize;
            int timescaleOffset;
            int durationOffset;

            switch (version)
            {
                case 0:
                    requiredSize = 20;
                    timescaleOffset = 12;
                    durationOffset = 16;
                    break;

                case 1:
                    requiredSize = 32;
                    timescaleOffset = 20;
                    durationOffset = 24;
                    break;

                default:
                    throw new Mp4ParsingException(
                        $"Unsupported mvhd version: {version}.");
            }

            if (box.DataSize < requiredSize)
            {
                throw new Mp4ParsingException(
                    "The mvhd box is too small for its version.");
            }

            byte[] data = new byte[requiredSize];

            stream.Position = box.DataOffset;
            stream.ReadExactly(data);

            uint timescale =
                BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(timescaleOffset, 4));

            ulong duration = version == 0
                ? BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(durationOffset, 4))
                : BinaryPrimitives.ReadUInt64BigEndian(
                    data.AsSpan(durationOffset, 8));

            if (timescale == 0)
            {
                throw new Mp4ParsingException(
                    "The mvhd box contains an invalid timescale.");
            }

            return new Mp4MovieHeader(timescale, duration);
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading mvhd.", ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}