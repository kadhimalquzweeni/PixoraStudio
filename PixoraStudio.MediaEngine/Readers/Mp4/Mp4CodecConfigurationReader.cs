
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads AVC and HEVC codec configuration boxes.
/// </summary>
public sealed class Mp4CodecConfigurationReader
{
    public Mp4CodecConfiguration Read(
        Stream stream,
        Mp4Box configurationBox)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(configurationBox);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        Mp4VideoCodec codec = configurationBox.Type switch
        {
            "avcC" => Mp4VideoCodec.Avc,
            "hvcC" => Mp4VideoCodec.Hevc,
            _ => throw new Mp4ParsingException(
                $"Unsupported codec configuration box '{configurationBox.Type}'.")
        };

        long originalPosition = stream.Position;

        try
        {
            if (configurationBox.DataSize > int.MaxValue)
            {
                throw new Mp4ParsingException(
                    "The codec configuration is too large.");
            }

            long end = checked(
                configurationBox.DataOffset +
                configurationBox.DataSize);

            if (configurationBox.DataOffset < 0 ||
                end > stream.Length)
            {
                throw new Mp4ParsingException(
                    "The codec configuration extends beyond the stream.");
            }

            byte[] data = new byte[
                checked((int)configurationBox.DataSize)];

            stream.Position = configurationBox.DataOffset;
            stream.ReadExactly(data);

            return codec switch
            {
                Mp4VideoCodec.Avc => ReadAvc(data),
                Mp4VideoCodec.Hevc => ReadHevc(data),
                _ => throw new Mp4ParsingException(
                    "Unsupported video codec.")
            };
        }
        catch (EndOfStreamException exception)
        {
            throw new Mp4ParsingException(
                "Unexpected end of codec configuration data.",
                exception);
        }
        catch (OverflowException exception)
        {
            throw new Mp4ParsingException(
                "Invalid codec configuration boundaries.",
                exception);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static Mp4CodecConfiguration ReadAvc(byte[] data)
    {
        if (data.Length < 7)
        {
            throw new Mp4ParsingException(
                "The avcC configuration is incomplete.");
        }

        byte version = data[0];

        if (version != 1)
        {
            throw new Mp4ParsingException(
                $"Unsupported AVC configuration version: {version}.");
        }

        int naluLengthSize = (data[4] & 0x03) + 1;

        if (naluLengthSize == 3)
        {
            throw new Mp4ParsingException(
                "The AVC configuration specifies an invalid NAL unit length size.");
        }

        int position = 6;
        int spsCount = data[5] & 0x1F;

        for (int i = 0; i < spsCount; i++)
        {
            position = SkipLengthPrefixedUnit(data, position);
        }

        if (position >= data.Length)
        {
            throw new Mp4ParsingException(
                "The AVC configuration is missing its PPS count.");
        }

        int ppsCount = data[position++];

        for (int i = 0; i < ppsCount; i++)
        {
            position = SkipLengthPrefixedUnit(data, position);
        }

        return new Mp4CodecConfiguration(
            Mp4VideoCodec.Avc,
            "avcC",
            data,
            version,
            data[1],
            data[2],
            data[3],
            naluLengthSize);
    }

    private static Mp4CodecConfiguration ReadHevc(byte[] data)
    {
        if (data.Length < 23)
        {
            throw new Mp4ParsingException(
                "The hvcC configuration is incomplete.");
        }

        byte version = data[0];

        if (version != 1)
        {
            throw new Mp4ParsingException(
                $"Unsupported HEVC configuration version: {version}.");
        }

        int naluLengthSize = (data[21] & 0x03) + 1;
        int arrayCount = data[22];
        int position = 23;

        for (int i = 0; i < arrayCount; i++)
        {
            if (data.Length - position < 3)
            {
                throw new Mp4ParsingException(
                    "Incomplete HEVC NAL array header.");
            }

            position++; // Array completeness and NAL unit type.

            ushort unitCount =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(position, 2));

            position += 2;

            for (int j = 0; j < unitCount; j++)
            {
                position = SkipLengthPrefixedUnit(data, position);
            }
        }

        return new Mp4CodecConfiguration(
            Mp4VideoCodec.Hevc,
            "hvcC",
            data,
            version,
            (byte)(data[1] & 0x1F),
            data[2],
            data[12],
            naluLengthSize);
    }

    private static int SkipLengthPrefixedUnit(
        byte[] data,
        int position)
    {
        if (data.Length - position < 2)
        {
            throw new Mp4ParsingException(
                "Incomplete codec NAL unit length.");
        }

        ushort length =
            BinaryPrimitives.ReadUInt16BigEndian(
                data.AsSpan(position, 2));

        position += 2;

        if (length > data.Length - position)
        {
            throw new Mp4ParsingException(
                "Codec NAL unit extends beyond its configuration box.");
        }

        return position + length;
    }
}
