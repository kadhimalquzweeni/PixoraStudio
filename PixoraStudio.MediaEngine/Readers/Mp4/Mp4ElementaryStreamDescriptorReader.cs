using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads MPEG-4 Elementary Stream Descriptors from an esds box.
/// </summary>
public sealed class Mp4ElementaryStreamDescriptorReader
{
    public Mp4AudioSpecificConfig Read(
        Stream stream,
        Mp4Box esdsBox)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(esdsBox);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        if (esdsBox.Type != "esds")
        {
            throw new Mp4ParsingException(
                "Expected an esds box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (esdsBox.DataSize < 4 ||
                esdsBox.DataSize > int.MaxValue)
            {
                throw new Mp4ParsingException(
                    "The esds box has an invalid size.");
            }

            long end = checked(
                esdsBox.DataOffset + esdsBox.DataSize);

            if (esdsBox.DataOffset < 0 || end > stream.Length)
            {
                throw new Mp4ParsingException(
                    "The esds box extends beyond the stream.");
            }

            byte[] data = new byte[(int)esdsBox.DataSize];

            stream.Position = esdsBox.DataOffset;
            stream.ReadExactly(data);

            if (data[0] != 0 ||
                data[1] != 0 ||
                data[2] != 0 ||
                data[3] != 0)
            {
                throw new Mp4ParsingException(
                    "Unsupported esds version or flags.");
            }

            byte[] audioSpecificData = FindAudioSpecificConfig(
                data,
                4,
                data.Length);

            return ParseAudioSpecificConfig(audioSpecificData);
        }
        catch (EndOfStreamException exception)
        {
            throw new Mp4ParsingException(
                "Unexpected end of esds data.",
                exception);
        }
        catch (OverflowException exception)
        {
            throw new Mp4ParsingException(
                "Invalid esds boundaries.",
                exception);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static byte[] FindAudioSpecificConfig(
        byte[] data,
        int start,
        int end)
    {
        int position = start;

        while (position < end)
        {
            Descriptor descriptor = ReadDescriptor(
                data,
                ref position,
                end);

            switch (descriptor.Tag)
            {
                case 0x03: // ES_Descriptor
                    {
                        int nestedStart = descriptor.Start;

                        RequireAvailable(
                            nestedStart,
                            3,
                            descriptor.End);

                        nestedStart += 2; // ES_ID

                        byte flags = data[nestedStart++];

                        if ((flags & 0x80) != 0)
                        {
                            nestedStart = Advance(
                                nestedStart, 2, descriptor.End);
                        }

                        if ((flags & 0x40) != 0)
                        {
                            RequireAvailable(
                                nestedStart, 1, descriptor.End);

                            int urlLength = data[nestedStart++];

                            nestedStart = Advance(
                                nestedStart,
                                urlLength,
                                descriptor.End);
                        }

                        if ((flags & 0x20) != 0)
                        {
                            nestedStart = Advance(
                                nestedStart, 2, descriptor.End);
                        }

                        byte[] result = FindAudioSpecificConfig(
                            data,
                            nestedStart,
                            descriptor.End);

                        return result;
                    }

                case 0x04: // DecoderConfigDescriptor
                    {
                        // Object type indication, stream type, buffer size,
                        // maximum bitrate and average bitrate.
                        RequireAvailable(
                            descriptor.Start,
                            13,
                            descriptor.End);

                        if (data[descriptor.Start] != 0x40)
                        {
                            throw new Mp4ParsingException(
                                "The mp4a entry does not contain an AAC decoder configuration.");
                        }

                        int nestedStart = Advance(
                            descriptor.Start,
                            13,
                            descriptor.End);

                        return FindAudioSpecificConfig(
                            data,
                            nestedStart,
                            descriptor.End);
                    }

                case 0x05: // DecoderSpecificInfo
                    {
                        if (descriptor.Length == 0)
                        {
                            throw new Mp4ParsingException(
                                "The DecoderSpecificInfo descriptor is empty.");
                        }

                        return data.AsSpan(
                            descriptor.Start,
                            descriptor.Length).ToArray();
                    }
            }
        }

        throw new Mp4ParsingException(
            "The esds box does not contain an AudioSpecificConfig descriptor.");
    }

    private static Descriptor ReadDescriptor(
        byte[] data,
        ref int position,
        int end)
    {
        RequireAvailable(position, 2, end);

        byte tag = data[position++];

        int length = 0;
        bool completed = false;

        for (int i = 0; i < 4; i++)
        {
            RequireAvailable(position, 1, end);

            byte value = data[position++];

            length = checked(
                (length << 7) | (value & 0x7F));

            if ((value & 0x80) == 0)
            {
                completed = true;
                break;
            }
        }

        if (!completed)
        {
            throw new Mp4ParsingException(
                "Invalid MPEG-4 descriptor length.");
        }

        if (length > end - position)
        {
            throw new Mp4ParsingException(
                "MPEG-4 descriptor extends beyond its parent.");
        }

        Descriptor descriptor = new(
            tag,
            position,
            length);

        position += length;

        return descriptor;
    }

    private static int Advance(
        int position,
        int count,
        int end)
    {
        RequireAvailable(position, count, end);
        return checked(position + count);
    }

    private static void RequireAvailable(
        int position,
        int count,
        int end)
    {
        if (position < 0 ||
            count < 0 ||
            position > end ||
            count > end - position)
        {
            throw new Mp4ParsingException(
                "Incomplete MPEG-4 descriptor.");
        }
    }

    private static Mp4AudioSpecificConfig ParseAudioSpecificConfig(
        byte[] data)
    {
        BitReader reader = new(data);

        int audioObjectType = reader.ReadBits(5);

        if (audioObjectType == 31)
        {
            audioObjectType = 32 + reader.ReadBits(6);
        }

        if (audioObjectType == 0 || audioObjectType > byte.MaxValue)
        {
            throw new Mp4ParsingException(
                "Invalid AAC audio object type.");
        }

        int frequencyIndex = reader.ReadBits(4);
        uint samplingFrequency;

        if (frequencyIndex == 15)
        {
            samplingFrequency = (uint)reader.ReadBits(24);

            if (samplingFrequency == 0)
            {
                throw new Mp4ParsingException(
                    "Invalid explicit AAC sampling frequency.");
            }
        }
        else
        {
            samplingFrequency = GetSamplingFrequency(
                frequencyIndex);
        }

        int channelConfiguration = reader.ReadBits(4);

        int channelCount = channelConfiguration switch
        {
            0 => 0, // Program Config Element required.
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            6 => 6,
            7 => 8,
            _ => throw new Mp4ParsingException(
                "Invalid AAC channel configuration.")
        };

        return new Mp4AudioSpecificConfig(
            Mp4AudioCodec.Aac,
            checked((byte)audioObjectType),
            samplingFrequency,
            checked((byte)frequencyIndex),
            checked((byte)channelConfiguration),
            channelCount,
            data);
    }

    private static uint GetSamplingFrequency(int index)
    {
        uint[] frequencies =
        [
            96000,
            88200,
            64000,
            48000,
            44100,
            32000,
            24000,
            22050,
            16000,
            12000,
            11025,
            8000,
            7350
        ];

        if (index < 0 || index >= frequencies.Length)
        {
            throw new Mp4ParsingException(
                "The AAC sampling frequency index is reserved.");
        }

        return frequencies[index];
    }

    private readonly record struct Descriptor(
        byte Tag,
        int Start,
        int Length)
    {
        public int End => checked(Start + Length);
    }

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _bitPosition;

        public BitReader(byte[] data)
        {
            _data = data;
        }

        public int ReadBits(int count)
        {
            if (count is < 1 or > 24 ||
                count > (_data.Length * 8) - _bitPosition)
            {
                throw new Mp4ParsingException(
                    "Incomplete AAC AudioSpecificConfig.");
            }

            int value = 0;

            for (int i = 0; i < count; i++)
            {
                int byteIndex = _bitPosition / 8;
                int bitIndex = 7 - (_bitPosition % 8);

                value = (value << 1) |
                    ((_data[byteIndex] >> bitIndex) & 1);

                _bitPosition++;
            }

            return value;
        }
    }
}