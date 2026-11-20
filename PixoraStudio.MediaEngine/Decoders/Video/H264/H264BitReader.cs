using System.Numerics;

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
    public void ConsumeRbspTrailingBits() => ConsumeRbspTrailingBitsCore(allowTrailingZeroBytes: false);

    /// <summary>
    /// Consumes rbsp_trailing_bits and permits complete zero bytes after them.
    /// Some MP4 AVC configuration records include this byte-stream padding
    /// inside the length-delimited SPS NAL unit.
    /// </summary>
    public void ConsumeRbspTrailingBitsAndZeroPadding() =>
        ConsumeRbspTrailingBitsCore(allowTrailingZeroBytes: true);

    private void ConsumeRbspTrailingBitsCore(bool allowTrailingZeroBytes)
    {
        if (RemainingBits == 0 || !ReadFlag())
            throw CreateTrailingBitsException("RBSP trailing bits must begin with rbsp_stop_one_bit.");
        while (!IsByteAligned)
        {
            if (ReadFlag())
                throw CreateTrailingBitsException("RBSP alignment bits must be zero.");
        }

        if (RemainingBits == 0)
            return;

        if (allowTrailingZeroBytes && RemainingBits % 8 == 0)
        {
            while (RemainingBits > 0)
            {
                if (ReadBits(8) != 0)
                    throw CreateTrailingBitsException("Trailing SPS padding bytes must be zero.");
            }
            return;
        }

        throw CreateTrailingBitsException("Unexpected data remains after rbsp_trailing_bits.");
    }

    /// <summary>
    /// Consumes CABAC rbsp_slice_trailing_bits followed by zero or more
    /// 16-bit cabac_zero_word values.
    /// </summary>
    public void ConsumeCabacTrailingBits()
    {
        if (RemainingBits == 0 || !ReadFlag())
            throw CreateTrailingBitsException(
                "CABAC slice trailing bits must begin with rbsp_stop_one_bit.");

        while (!IsByteAligned)
        {
            if (ReadFlag())
                throw CreateTrailingBitsException(
                    "CABAC slice alignment bits must be zero.");
        }

        if (RemainingBits % 16 != 0)
            throw CreateTrailingBitsException(
                "CABAC cabac_zero_word padding must contain complete 16-bit words.");

        while (RemainingBits > 0)
        {
            if (ReadBits(16) != 0)
                throw CreateTrailingBitsException(
                    "CABAC cabac_zero_word values must be zero.");
        }
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

        ReadOnlySpan<byte> data = _data.Span;
        int position = _position;
        uint value = ReadBitsCore(data, ref position, count);
        _position = position;
        return value;
    }

    /// <summary>
    /// Reads one bit using a dedicated fast path for CABAC and other bit-level hot loops.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal int ReadBit()
    {
        int position = _position;
        if ((uint)position >= (uint)_lengthBits)
            throw new EndOfStreamException("The H.264 bitstream ended unexpectedly.");

        int value = (_data.Span[position >> 3] >> (7 - (position & 7))) & 1;
        _position = position + 1;
        return value;
    }

    public bool ReadFlag() => ReadBit() != 0;

    /// <summary>Reads bits at an offset from the current position without advancing the reader.</summary>
    public uint PeekBits(int count, int bitOffset = 0)
    {
        if (count is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(count));
        if (bitOffset < 0) throw new ArgumentOutOfRangeException(nameof(bitOffset));
        if (bitOffset > RemainingBits || count > RemainingBits - bitOffset)
            throw new EndOfStreamException("The H.264 bitstream ended unexpectedly.");

        int position = _position + bitOffset;
        return ReadBitsCore(_data.Span, ref position, count);
    }

    /// <summary>
    /// Reads up to 32 bits a byte-chunk at a time. This avoids a loop iteration,
    /// bounds calculation and span access for every individual bit.
    /// </summary>
    private static uint ReadBitsCore(ReadOnlySpan<byte> data, ref int position, int count)
    {
        uint value = 0;
        int remaining = count;

        while (remaining > 0)
        {
            int bitOffset = position & 7;
            int bitsFromByte = Math.Min(remaining, 8 - bitOffset);
            int shift = 8 - bitOffset - bitsFromByte;
            uint mask = (1u << bitsFromByte) - 1u;
            uint chunk = ((uint)data[position >> 3] >> shift) & mask;

            value = (value << bitsFromByte) | chunk;
            position += bitsFromByte;
            remaining -= bitsFromByte;
        }

        return value;
    }

    public uint ReadUnsignedExpGolomb()
    {
        int availableBits = Math.Min(RemainingBits, 32);
        if (availableBits == 0)
            throw new EndOfStreamException("Truncated unsigned Exp-Golomb value.");

        // Locate the first one bit in one peek rather than consuming the prefix
        // one bit at a time. Padding the peek on the right lets LeadingZeroCount
        // handle both byte-aligned and unaligned codewords uniformly.
        uint prefix = PeekBits(availableBits);
        int leadingZeroBits = BitOperations.LeadingZeroCount(prefix << (32 - availableBits));

        if (leadingZeroBits >= availableBits)
        {
            if (availableBits == 32)
                throw new FormatException("Unsigned Exp-Golomb value exceeds UInt32.");
            throw new EndOfStreamException("Truncated unsigned Exp-Golomb value.");
        }

        if (leadingZeroBits > 31)
            throw new FormatException("Unsigned Exp-Golomb value exceeds UInt32.");

        SkipBits(leadingZeroBits + 1);
        if (leadingZeroBits == 0)
            return 0;

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
