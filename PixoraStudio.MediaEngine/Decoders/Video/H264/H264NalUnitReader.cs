using System.Buffers.Binary;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Splits H.264 access-unit data into NAL units.</summary>
public sealed class H264NalUnitReader
{
    public IReadOnlyList<H264NalUnit> ReadAnnexB(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        List<H264NalUnit> units = [];
        int position = 0;

        while (position < span.Length)
        {
            int prefixStart = FindStartCode(span, position, out int prefixLength);
            if (prefixStart < 0)
            {
                if (units.Count == 0 && !IsAllZero(span))
                    throw new FormatException("No Annex B start code was found.");
                break;
            }

            int nalStart = prefixStart + prefixLength;
            int nextPrefix = FindStartCode(span, nalStart, out _);
            int nalEnd = nextPrefix < 0 ? span.Length : nextPrefix;
            while (nalEnd > nalStart && span[nalEnd - 1] == 0) nalEnd--;

            if (nalEnd > nalStart)
                units.Add(CreateUnit(span[nalStart..nalEnd]));
            else
                throw new FormatException("Annex B stream contains an empty NAL unit.");

            position = nextPrefix < 0 ? span.Length : nextPrefix;
        }

        if (units.Count == 0) throw new FormatException("Annex B stream contains no NAL units.");
        return units;
    }

    public IReadOnlyList<H264NalUnit> ReadLengthPrefixed(ReadOnlyMemory<byte> data, int lengthFieldSize)
    {
        if (lengthFieldSize is not (1 or 2 or 4))
            throw new ArgumentOutOfRangeException(nameof(lengthFieldSize), "NAL length fields must be 1, 2, or 4 bytes.");

        ReadOnlySpan<byte> span = data.Span;
        List<H264NalUnit> units = [];
        int position = 0;

        while (position < span.Length)
        {
            if (span.Length - position < lengthFieldSize)
                throw new FormatException("Truncated NAL unit length field.");

            uint length = lengthFieldSize switch
            {
                1 => span[position],
                2 => BinaryPrimitives.ReadUInt16BigEndian(span.Slice(position, 2)),
                4 => BinaryPrimitives.ReadUInt32BigEndian(span.Slice(position, 4)),
                _ => throw new UnreachableException()
            };
            position += lengthFieldSize;

            if (length == 0) throw new FormatException("NAL unit length cannot be zero.");
            if (length > span.Length - position)
                throw new FormatException("NAL unit extends beyond the supplied sample.");

            units.Add(CreateUnit(span.Slice(position, checked((int)length))));
            position += checked((int)length);
        }

        return units;
    }

    private static H264NalUnit CreateUnit(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) throw new FormatException("NAL unit is empty.");
        byte header = bytes[0];
        if ((header & 0x80) != 0) throw new FormatException("H.264 forbidden_zero_bit must be zero.");
        byte type = (byte)(header & 0x1F);
        if (type is 0 or > 23) throw new FormatException($"Reserved or unspecified H.264 NAL unit type: {type}.");
        return new H264NalUnit(header, bytes[1..]);
    }

    private static int FindStartCode(ReadOnlySpan<byte> data, int start, out int length)
    {
        for (int i = start; i <= data.Length - 3; i++)
        {
            if (data[i] != 0 || data[i + 1] != 0) continue;
            if (data[i + 2] == 1) { length = 3; return i; }
            if (i <= data.Length - 4 && data[i + 2] == 0 && data[i + 3] == 1)
            { length = 4; return i; }
        }
        length = 0;
        return -1;
    }

    private static bool IsAllZero(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data) if (value != 0) return false;
        return true;
    }
}
