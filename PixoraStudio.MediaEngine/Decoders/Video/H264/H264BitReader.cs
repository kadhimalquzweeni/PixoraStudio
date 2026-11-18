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

    /// <summary>
    /// Returns false only when the remaining bits are exactly rbsp_trailing_bits:
    /// one stop bit followed by zero bits to the end of the RBSP.
    /// </summary>
    public bool MoreRbspData()
    {
        if (RemainingBits == 0)
            return false;
        if (!PeekBits(1).Equals(1u))
            return true;

        for (int offset = 1; offset < RemainingBits; offset++)
            if (PeekBits(1, offset) != 0)
                return true;
        return false;
    }

    /// <summary>Consumes and validates rbsp_stop_one_bit and alignment zero bits.</summary>
    public void ConsumeRbspTrailingBits()
    {
        if (RemainingBits == 0 || !ReadFlag())
            throw CreateTrailingBitsException("RBSP trailing bits must begin with rbsp_stop_one_bit.");
        while (!IsByteAligned)
        {
            if (ReadFlag())
                throw CreateTrailingBitsException("RBSP alignment bits must be zero.");
        }
        if (RemainingBits != 0)
            throw CreateTrailingBitsException("Unexpected data remains after rbsp_trailing_bits.");
    }

    private FormatException CreateTrailingBitsException(string reason)
    {
        char[] remainingBitCharacters = new char[RemainingBits];
        for (int i = 0; i < remainingBitCharacters.Length; i++)
        {
            int bit = _position + i;
            remainingBitCharacters[i] = ((_data.Span[bit >> 3] >> (7 - (bit & 7))) & 1) == 0 ? '0' : '1';
        }
        string remainingBits = new(remainingBitCharacters);

        return new FormatException(
            $"{reason} Bit position: {_position}; total RBSP bits: {_lengthBits}; " +
            $"remaining bits: {RemainingBits}; remaining bit sequence: {remainingBits}; " +
            $"RBSP hex: {Convert.ToHexString(_data.Span)}.");
    }

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
        long value = (codeNum & 1) == 0
            ? -(long)(codeNum / 2u)
            : ((long)codeNum + 1) / 2;
        if (value < int.MinValue || value > int.MaxValue)
            throw new FormatException("Signed Exp-Golomb value exceeds Int32.");
        return (int)value;
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
