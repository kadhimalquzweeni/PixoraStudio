using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads edit-list entries from an MP4 elst box.
/// </summary>
public sealed class Mp4EditListReader
{
    private const uint MaximumEntryCount = 1_000_000;

    public Mp4EditList Read(Stream stream, Mp4Box box)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(box);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        if (box.Type != "elst")
        {
            throw new Mp4ParsingException(
                "Expected an elst box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The elst box extends beyond the stream.");
            }

            if (box.DataSize < 8)
            {
                throw new Mp4ParsingException(
                    "The elst box is too small.");
            }

            byte[] header = new byte[8];

            stream.Position = box.DataOffset;
            stream.ReadExactly(header);

            int version = header[0];

            if (version is not 0 and not 1)
            {
                throw new Mp4ParsingException(
                    $"Unsupported elst version: {version}.");
            }

            uint entryCount =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(4, 4));

            if (entryCount > MaximumEntryCount)
            {
                throw new Mp4ParsingException(
                    "The edit list contains too many entries.");
            }

            int entrySize = version == 0 ? 12 : 20;

            ulong requiredSize = checked(
                8UL + ((ulong)entryCount * (uint)entrySize));

            if (requiredSize > (ulong)box.DataSize)
            {
                throw new Mp4ParsingException(
                    "The edit list entries extend beyond the box.");
            }

            List<Mp4EditListEntry> entries = new(
                checked((int)entryCount));

            byte[] data = new byte[entrySize];

            for (uint i = 0; i < entryCount; i++)
            {
                stream.ReadExactly(data);

                ulong segmentDuration;
                long mediaTime;
                short rateInteger;
                short rateFraction;

                if (version == 0)
                {
                    segmentDuration =
                        BinaryPrimitives.ReadUInt32BigEndian(
                            data.AsSpan(0, 4));

                    mediaTime =
                        BinaryPrimitives.ReadInt32BigEndian(
                            data.AsSpan(4, 4));

                    rateInteger =
                        BinaryPrimitives.ReadInt16BigEndian(
                            data.AsSpan(8, 2));

                    rateFraction =
                        BinaryPrimitives.ReadInt16BigEndian(
                            data.AsSpan(10, 2));
                }
                else
                {
                    segmentDuration =
                        BinaryPrimitives.ReadUInt64BigEndian(
                            data.AsSpan(0, 8));

                    mediaTime =
                        BinaryPrimitives.ReadInt64BigEndian(
                            data.AsSpan(8, 8));

                    rateInteger =
                        BinaryPrimitives.ReadInt16BigEndian(
                            data.AsSpan(16, 2));

                    rateFraction =
                        BinaryPrimitives.ReadInt16BigEndian(
                            data.AsSpan(18, 2));
                }

                if (mediaTime < -1)
                {
                    throw new Mp4ParsingException(
                        "The edit list contains an invalid media time.");
                }

                if (rateInteger != 1 || rateFraction != 0)
                {
                    throw new Mp4ParsingException(
                        "Only normal-speed edit-list playback is supported.");
                }

                entries.Add(
                    new Mp4EditListEntry(
                        segmentDuration,
                        mediaTime,
                        rateInteger,
                        rateFraction));
            }

            return new Mp4EditList(entries);
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading elst.", ex);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                "The edit list contains invalid sizes.", ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}