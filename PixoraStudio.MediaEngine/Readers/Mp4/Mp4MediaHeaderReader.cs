
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads timing information from an MP4 media header (mdhd) box.
/// </summary>
public sealed class Mp4MediaHeaderReader
{
    public (uint Timescale, ulong DurationTicks) Read(
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

        if (box.Type != "mdhd")
        {
            throw new Mp4ParsingException(
                "Expected an mdhd box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The mdhd box extends beyond the stream.");
            }

            if (box.DataSize < 1)
            {
                throw new Mp4ParsingException(
                    "The mdhd box does not contain a version.");
            }

            stream.Position = box.DataOffset;

            int versionValue = stream.ReadByte();

            if (versionValue < 0)
            {
                throw new Mp4ParsingException(
                    "Unable to read the mdhd version.");
            }

            int requiredSize;
            int timescaleOffset;
            int durationOffset;

            switch (versionValue)
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
                        $"Unsupported mdhd version: {versionValue}.");
            }

            if (box.DataSize < requiredSize)
            {
                throw new Mp4ParsingException(
                    "The mdhd box is too small for its version.");
            }

            byte[] data = new byte[requiredSize];

            stream.Position = box.DataOffset;
            stream.ReadExactly(data);

            uint timescale =
                BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(timescaleOffset, 4));

            ulong durationTicks;

            if (versionValue == 0)
            {
                durationTicks =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        data.AsSpan(durationOffset, 4));
            }
            else
            {
                durationTicks =
                    BinaryPrimitives.ReadUInt64BigEndian(
                        data.AsSpan(durationOffset, 8));
            }

            if (timescale == 0)
            {
                throw new Mp4ParsingException(
                    "The mdhd box contains an invalid timescale.");
            }

            return (timescale, durationTicks);
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading mdhd.", ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
