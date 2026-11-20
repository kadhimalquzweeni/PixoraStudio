using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Writes a version-one edit list for an exported track.
/// </summary>
public sealed class Mp4EditListBoxWriter
{
    private readonly Mp4BoxWriter _boxWriter = new();

    public void Write(Stream destination, IEnumerable<Mp4EditListEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(entries);

        Mp4EditListEntry[] values = entries.ToArray();
        if (values.Length == 0)
            throw new ArgumentException("An edit list must contain at least one entry.", nameof(entries));

        _boxWriter.WriteBox(destination, "edts", edts =>
            _boxWriter.WriteFullBox(edts, "elst", 1, 0, body =>
            {
                WriteUInt32(body, checked((uint)values.Length));
                foreach (Mp4EditListEntry entry in values)
                {
                    if (entry.MediaTime < -1 || !entry.UsesNormalPlaybackRate)
                        throw new Mp4ParsingException("The export edit list contains an unsupported entry.");

                    WriteUInt64(body, entry.SegmentDuration);
                    WriteInt64(body, entry.MediaTime);
                    WriteInt16(body, entry.MediaRateInteger);
                    WriteInt16(body, entry.MediaRateFraction);
                }
            }));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt16(Stream stream, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
