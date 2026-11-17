
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads track information from an MP4 track header (tkhd) box.
/// </summary>
public sealed class Mp4TrackHeaderReader
{
    public Mp4Track Read(Stream stream, Mp4Box box)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(box);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        if (box.Type != "tkhd")
        {
            throw new Mp4ParsingException(
                "Expected a tkhd box.");
        }

        long originalPosition = stream.Position;

        try
        {
            byte version = ReadVersion(stream, box);

            int requiredSize;
            int trackIdOffset;
            int dimensionsOffset;

            switch (version)
            {
                case 0:
                    requiredSize = 84;
                    trackIdOffset = 12;
                    dimensionsOffset = 76;
                    break;

                case 1:
                    requiredSize = 96;
                    trackIdOffset = 20;
                    dimensionsOffset = 88;
                    break;

                default:
                    throw new Mp4ParsingException(
                        $"Unsupported tkhd version: {version}.");
            }

            if (box.DataSize < requiredSize)
            {
                throw new Mp4ParsingException(
                    "The tkhd box is too small for its version.");
            }

            if (box.DataOffset < 0 ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The tkhd box extends beyond the stream.");
            }

            byte[] data = new byte[requiredSize];

            stream.Position = box.DataOffset;
            stream.ReadExactly(data);

            uint trackId =
                BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(trackIdOffset, 4));

            uint rawWidth =
                BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(dimensionsOffset, 4));

            uint rawHeight =
                BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(dimensionsOffset + 4, 4));

            if (trackId == 0)
            {
                throw new Mp4ParsingException(
                    "The tkhd box contains an invalid track ID.");
            }

            double width = rawWidth / 65536.0;
            double height = rawHeight / 65536.0;

            return new Mp4Track(
                trackId,
                width,
                height);
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading tkhd.", ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static byte ReadVersion(
        Stream stream,
        Mp4Box box)
    {
        if (box.DataSize < 1)
        {
            throw new Mp4ParsingException(
                "The tkhd box does not contain a version.");
        }

        stream.Position = box.DataOffset;

        int value = stream.ReadByte();

        if (value < 0)
        {
            throw new Mp4ParsingException(
                "Unable to read the tkhd version.");
        }

        return (byte)value;
    }
}