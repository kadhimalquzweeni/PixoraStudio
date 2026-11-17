
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Exceptions;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads sample entries from an MP4 sample description box.
/// </summary>
public sealed class Mp4SampleDescriptionReader
{
    private const uint MaximumEntryCount = 10_000_000;

    /// <summary>
    /// Reads the sample entries contained in an stsd box.
    /// </summary>
    public IReadOnlyList<Mp4Box> Read(
        Stream stream,
        Mp4Box stsdBox)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(stsdBox);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        if (stsdBox.Type != "stsd")
        {
            throw new Mp4ParsingException(
                "Expected an stsd box.");
        }

        long originalPosition = stream.Position;

        try
        {
            long boxEnd = checked(
                stsdBox.Offset + (long)stsdBox.Size);

            if (boxEnd > stream.Length)
            {
                throw new Mp4ParsingException(
                    "The stsd box extends beyond the stream boundary.");
            }

            if (stsdBox.DataSize < 8)
            {
                throw new Mp4ParsingException(
                    "The stsd box does not contain a complete header.");
            }

            stream.Position = stsdBox.DataOffset;

            // Read FullBox version and flags.
            byte[] fullBoxHeader = new byte[4];
            stream.ReadExactly(fullBoxHeader);

            byte version = fullBoxHeader[0];

            if (version != 0)
            {
                throw new Mp4ParsingException(
                    $"Unsupported stsd version: {version}.");
            }

            if (fullBoxHeader[1] != 0 ||
                fullBoxHeader[2] != 0 ||
                fullBoxHeader[3] != 0)
            {
                throw new Mp4ParsingException(
                    "The stsd box contains unsupported flags.");
            }

            // Read the number of sample entries.
            byte[] countBuffer = new byte[4];
            stream.ReadExactly(countBuffer);

            uint entryCount =
                BinaryPrimitives.ReadUInt32BigEndian(countBuffer);

            if (entryCount > MaximumEntryCount)
            {
                throw new Mp4ParsingException(
                    "The stsd entry count exceeds the supported limit.");
            }

            List<Mp4Box> entries = new(
                checked((int)entryCount));

            for (uint index = 0; index < entryCount; index++)
            {
                long entryOffset = stream.Position;
                long remaining = boxEnd - entryOffset;

                if (remaining < 8)
                {
                    throw new Mp4ParsingException(
                        "Incomplete sample entry header.");
                }

                byte[] header = new byte[8];
                stream.ReadExactly(header);

                uint size32 =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        header.AsSpan(0, 4));

                string type = Encoding.ASCII.GetString(
                    header,
                    4,
                    4);

                ulong entrySize;
                long headerSize = 8;
                bool extended = false;
                bool extendsToEnd = false;

                if (size32 == 1)
                {
                    if (remaining < 16)
                    {
                        throw new Mp4ParsingException(
                            "Incomplete extended sample entry header.");
                    }

                    byte[] extendedHeader = new byte[8];
                    stream.ReadExactly(extendedHeader);

                    entrySize =
                        BinaryPrimitives.ReadUInt64BigEndian(
                            extendedHeader);

                    headerSize = 16;
                    extended = true;
                }
                else if (size32 == 0)
                {
                    entrySize = checked((ulong)remaining);
                    extendsToEnd = true;
                }
                else
                {
                    entrySize = size32;
                }

                if (entrySize < (ulong)headerSize ||
                    entrySize > (ulong)remaining ||
                    entrySize > long.MaxValue)
                {
                    throw new Mp4ParsingException(
                        $"Invalid size for sample entry '{type}'.");
                }

                Mp4Box entry;

                try
                {
                    entry = new Mp4Box(
                        entrySize,
                        type,
                        entryOffset,
                        headerSize,
                        extended,
                        extendsToEnd);
                }
                catch (OverflowException exception)
                {
                    throw new Mp4ParsingException(
                        $"Invalid offsets for sample entry '{type}'.",
                        exception);
                }

                entries.Add(entry);

                stream.Position = checked(
                    entryOffset + (long)entrySize);

                if (extendsToEnd && index + 1 < entryCount)
                {
                    throw new Mp4ParsingException(
                        "A sample entry extending to the end cannot be followed by another entry.");
                }
            }

            if (stream.Position != boxEnd)
            {
                throw new Mp4ParsingException(
                    "The stsd entry count does not match its data size.");
            }

            return entries.AsReadOnly();
        }
        catch (EndOfStreamException exception)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading stsd.",
                exception);
        }
        catch (OverflowException exception)
        {
            throw new Mp4ParsingException(
                "Invalid stsd box boundaries.",
                exception);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}