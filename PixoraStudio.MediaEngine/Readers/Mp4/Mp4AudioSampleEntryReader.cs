using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads MPEG-4 audio sample entries.
/// </summary>
public sealed class Mp4AudioSampleEntryReader
{
    private const int AudioSampleEntrySize = 28;

    private readonly Mp4ElementaryStreamDescriptorReader
        _descriptorReader = new();

    public Mp4AudioSampleEntry Read(
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

        if (sampleEntry.Type != "mp4a")
        {
            throw new Mp4ParsingException(
                $"Unsupported audio sample entry '{sampleEntry.Type}'.");
        }

        if (sampleEntry.DataSize < AudioSampleEntrySize)
        {
            throw new Mp4ParsingException(
                "The audio sample entry is incomplete.");
        }

        long originalPosition = stream.Position;

        try
        {
            long entryEnd = checked(
                sampleEntry.Offset + (long)sampleEntry.Size);

            if (sampleEntry.Offset < 0 ||
                entryEnd > stream.Length)
            {
                throw new Mp4ParsingException(
                    "The audio sample entry extends beyond the stream.");
            }

            byte[] data = new byte[AudioSampleEntrySize];

            stream.Position = sampleEntry.DataOffset;
            stream.ReadExactly(data);

            ushort dataReferenceIndex =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(6, 2));

            ushort version =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(8, 2));

            if (version != 0)
            {
                throw new Mp4ParsingException(
                    $"Unsupported mp4a sample entry version: {version}.");
            }

            ushort channelCount =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(16, 2));

            ushort sampleSize =
                BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(18, 2));

            uint sampleRateFixed =
                BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(24, 4));

            double sampleRate =
                sampleRateFixed / 65536.0;

            Mp4Box esdsBox = FindEsdsBox(
                stream,
                sampleEntry,
                entryEnd);

            Mp4AudioSpecificConfig audioConfig =
                _descriptorReader.Read(stream, esdsBox);

            return new Mp4AudioSampleEntry(
                sampleEntry.Type,
                dataReferenceIndex,
                version,
                channelCount,
                sampleSize,
                sampleRate,
                audioConfig.Codec,
                audioConfig);
        }
        catch (EndOfStreamException exception)
        {
            throw new Mp4ParsingException(
                "Unexpected end of audio sample entry.",
                exception);
        }
        catch (OverflowException exception)
        {
            throw new Mp4ParsingException(
                "Invalid audio sample entry boundaries.",
                exception);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static Mp4Box FindEsdsBox(
        Stream stream,
        Mp4Box sampleEntry,
        long entryEnd)
    {
        long position = checked(
            sampleEntry.DataOffset + AudioSampleEntrySize);

        Mp4Box? esdsBox = null;

        while (position < entryEnd)
        {
            long remaining = entryEnd - position;

            if (remaining < 8)
            {
                throw new Mp4ParsingException(
                    "Incomplete child box header in audio sample entry.");
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
                        "Incomplete extended audio child box header.");
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

            if (type == "esds")
            {
                if (esdsBox is not null)
                {
                    throw new Mp4ParsingException(
                        "The audio sample entry contains multiple esds boxes.");
                }

                esdsBox = child;
            }

            position = checked(position + (long)size);

            if (extendsToEnd)
            {
                break;
            }
        }

        if (position != entryEnd)
        {
            throw new Mp4ParsingException(
                "Invalid child box boundaries in audio sample entry.");
        }

        return esdsBox ?? throw new Mp4ParsingException(
            "The mp4a sample entry has no esds box.");
    }
}