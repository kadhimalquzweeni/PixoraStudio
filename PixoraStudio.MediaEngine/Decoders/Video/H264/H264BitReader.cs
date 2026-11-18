namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Reads H.264 syntax elements from an RBSP in most-significant-bit-first order.</summary>
public sealed class H264BitReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _lengthBits;
    private int _position;

    public H264BitReader(ReadOnlyMemory<byte> data)
        : this(data, checked(data.Length * 8)) { }

    public H264BitReader(ReadOnlyMemory<byte> data, int validBitLength)
    {
        int capacity = checked(data.Length * 8);
        if (validBitLength < 0 || validBitLength > capacity)
            throw new ArgumentOutOfRangeException(nameof(validBitLength));
        _data = data;
        _lengthBits = validBitLength;
    }

    public int PositionBits => _position;
    public int RemainingBits => _lengthBits - _position;
    public bool IsByteAligned => (_position & 7) == 0;

    public uint ReadBits(int count)
    {
        if (count is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(count));
        if (count > RemainingBits) throw new EndOfStreamException("The H.264 bitstream ended unexpectedly.");
        uint value = 0;
        for (int i = 0; i < count; i++)
        {
            int byteIndex = _position >> 3;
            int bitIndex = 7 - (_position & 7);
            value = (value << 1) | (uint)((_data.Span[byteIndex] >> bitIndex) & 1);
            _position++;
        }
        return value;
    }

    public bool ReadFlag() => ReadBits(1) != 0;

    /// <summary>Reads bits at an offset from the current position without advancing the reader.</summary>
    public uint PeekBits(int count, int bitOffset = 0)
    {
        if (count is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(count));
        if (bitOffset < 0) throw new ArgumentOutOfRangeException(nameof(bitOffset));
        if (bitOffset > RemainingBits || count > RemainingBits - bitOffset)
            throw new EndOfStreamException("The H.264 bitstream ended unexpectedly.");

        uint value = 0;
        int position = _position + bitOffset;
        for (int i = 0; i < count; i++)
        {
            int byteIndex = position >> 3;
            int bitIndex = 7 - (position & 7);
            value = (value << 1) | (uint)((_data.Span[byteIndex] >> bitIndex) & 1);
            position++;
        }
        return value;
    }

    public uint ReadUnsignedExpGolomb()
    {
        int leadingZeroBits = 0;
        while (true)
        {
            if (RemainingBits == 0) throw new EndOfStreamException("Truncated unsigned Exp-Golomb value.");
            if (ReadFlag()) break;
            leadingZeroBits++;
            if (leadingZeroBits > 31) throw new FormatException("Unsigned Exp-Golomb value exceeds UInt32.");
        }

        if (leadingZeroBits == 0) return 0;
        uint suffix = ReadBits(leadingZeroBits);
        return checked(((1u << leadingZeroBits) - 1u) + suffix);
    }

    public int ReadSignedExpGolomb()
    {
        uint codeNum = ReadUnsignedExpGolomb();
        if (codeNum > int.MaxValue) throw new FormatException("Signed Exp-Golomb value exceeds Int32.");
        int magnitude = checked((int)((codeNum + 1u) >> 1));
        return (codeNum & 1) == 0 ? -magnitude : magnitude;
    }

    /// <summary>Consumes pcm_alignment_zero_bit values until the next byte boundary.</summary>
    public void AlignToByteAndValidateZero()
    {
        while (!IsByteAligned)
        {
            if (ReadFlag())
                throw new FormatException("PCM alignment bits must be zero.");
        }
    }

    public void SkipBits(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count > RemainingBits) throw new EndOfStreamException("Cannot skip beyond the H.264 bitstream.");
        _position += count;
    }
}
