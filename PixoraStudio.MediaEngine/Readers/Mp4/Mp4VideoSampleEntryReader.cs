
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads visual sample entries such as avc1 and hvc1.
/// </summary>
public sealed class Mp4VideoSampleEntryReader
{
    private const int VisualSampleEntrySize = 78;

    private readonly Mp4CodecConfigurationReader _configurationReader = new();

    public Mp4VideoSampleEntry Read(
        Stream stream,
        Mp4Box sampleEntry)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sampleEntry);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        Mp4VideoCodec codec = GetCodec(sampleEntry.Type);

        if (codec == Mp4VideoCodec.Unknown)
        {
            throw new Mp4ParsingException(
                $"Unsupported video sample entry '{sampleEntry.Type}'.");
        }

        if (sampleEntry.DataSize < VisualSampleEntrySize)
        {
            throw new Mp4ParsingException(
                "The visual sample entry is incomplete.");
        }

        long originalPosition = stream.Position;

        try
        {
            long entryEnd = checked(
                sampleEntry.Offset + (long)sampleEntry.Size);

            if (entryEnd > stream.Length)
            {
                throw new Mp4ParsingException(
                    "The video sample entry extends beyond the stream.");
            }

            byte[] data = new byte[VisualSampleEntrySize];

            stream.Position = sampleEntry.DataOffset;
            stream.ReadExactly(data);

            ushort dataReferenceIndex =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(6, 2));

            ushort width =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(24, 2));

            ushort height =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(26, 2));

            uint horizontalResolution =
                BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(28, 4));

            uint verticalResolution =
                BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(32, 4));

            ushort frameCount =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(40, 2));

            int compressorLength = Math.Min(data[42], (byte)31);

            string compressorName = Encoding.UTF8.GetString(
                data,
                43,
                compressorLength);

            ushort depth =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(74, 2));

            Mp4Box? configurationBox = FindConfigurationBox(
                stream,
                sampleEntry,
                entryEnd,
                VisualSampleEntrySize);

            if (configurationBox is null)
            {
                throw new Mp4ParsingException(
                    $"The '{sampleEntry.Type}' entry has no codec configuration box.");
            }

            string expectedConfiguration =
                codec == Mp4VideoCodec.Avc ? "avcC" : "hvcC";

            if (configurationBox.Type != expectedConfiguration)
            {
                throw new Mp4ParsingException(
                    $"The '{sampleEntry.Type}' entry requires '{expectedConfiguration}'.");
            }

            Mp4CodecConfiguration configuration =
                _configurationReader.Read(stream, configurationBox);

            return new Mp4VideoSampleEntry(
                sampleEntry.Type,
                dataReferenceIndex,
                width,
                height,
                horizontalResolution / 65536.0,
                verticalResolution / 65536.0,
                frameCount,
                compressorName,
                depth,
                codec,
                configuration);
        }
        catch (EndOfStreamException exception)
        {
            throw new Mp4ParsingException(
                "Unexpected end of video sample entry.",
                exception);
        }
        catch (OverflowException exception)
        {
            throw new Mp4ParsingException(
                "Invalid video sample entry boundaries.",
                exception);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static Mp4VideoCodec GetCodec(string format)
    {
        return format switch
        {
            "avc1" or "avc3" => Mp4VideoCodec.Avc,
            "hvc1" or "hev1" => Mp4VideoCodec.Hevc,
            _ => Mp4VideoCodec.Unknown
        };
    }

    private static Mp4Box? FindConfigurationBox(
        Stream stream,
        Mp4Box sampleEntry,
        long entryEnd,
        int fixedDataSize)
    {
        long position = checked(
            sampleEntry.DataOffset + fixedDataSize);

        Mp4Box? configurationBox = null;

        while (position < entryEnd)
        {
            long remaining = entryEnd - position;

            if (remaining < 8)
            {
                throw new Mp4ParsingException(
                    "Incomplete child box header in video sample entry.");
            }

            stream.Position = position;

            byte[] header = new byte[8];
            stream.ReadExactly(header);

            uint size32 =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(0, 4));

            string type = Encoding.ASCII.GetString(
                header,
                4,
                4);

            ulong size;
            long headerSize = 8;
            bool extended = false;
            bool extendsToEnd = false;

            if (size32 == 1)
            {
                if (remaining < 16)
                {
                    throw new Mp4ParsingException(
                        "Incomplete extended child box header.");
                }

                byte[] extendedHeader = new byte[8];
                stream.ReadExactly(extendedHeader);

                size = BinaryPrimitives.ReadUInt64BigEndian(
                    extendedHeader);

                headerSize = 16;
                extended = true;
            }
            else if (size32 == 0)
            {
                size = checked((ulong)remaining);
                extendsToEnd = true;
            }
            else
            {
                size = size32;
            }

            if (size < (ulong)headerSize ||
                size > (ulong)remaining ||
                size > long.MaxValue)
            {
                throw new Mp4ParsingException(
                    $"Invalid child box size for '{type}'.");
            }

            Mp4Box child = new(
                size,
                type,
                position,
                headerSize,
                extended,
                extendsToEnd);

            if (type is "avcC" or "hvcC")
            {
                if (configurationBox is not null)
                {
                    throw new Mp4ParsingException(
                        "The video sample entry contains multiple codec configuration boxes.");
                }

                configurationBox = child;
            }

            position = checked(position + (long)size);

            if (extendsToEnd)
                break;
        }

        if (position != entryEnd)
        {
            throw new Mp4ParsingException(
                "Invalid child box boundaries in video sample entry.");
        }

        return configurationBox;
    }
}
