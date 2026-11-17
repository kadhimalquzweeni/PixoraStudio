
namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads bits from a buffer in most-significant-bit-first order.
/// </summary>
public sealed class AacBitReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _lengthBits;
    private int _positionBits;

    /// <summary>
    /// Creates a reader that treats every bit in the buffer as valid.
    /// </summary>
    public AacBitReader(ReadOnlyMemory<byte> data)
        : this(data, checked(data.Length * 8))
    {
    }

    /// <summary>
    /// Creates a reader with an explicit number of valid bits.
    /// Bits after <paramref name="validBitLength"/> are inaccessible,
    /// even if they exist as padding in the final byte.
    /// </summary>
    public AacBitReader(
        ReadOnlyMemory<byte> data,
        int validBitLength)
    {
        int capacity = checked(data.Length * 8);

        if (validBitLength < 0 || validBitLength > capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(validBitLength),
                validBitLength,
                "The valid bit length must fit inside the supplied buffer.");
        }

        _data = data;
        _lengthBits = validBitLength;
    }

    public int PositionBits => _positionBits;

    public int LengthBits => _lengthBits;

    public int RemainingBits => _lengthBits - _positionBits;

    public bool IsByteAligned => (_positionBits & 7) == 0;

    /// <summary>
    /// Reads between zero and 32 bits.
    /// </summary>
    public uint ReadBits(int bitCount)
    {
        ValidateBitCount(bitCount);

        if (bitCount > RemainingBits)
        {
            throw new EndOfStreamException(
                "The AAC bitstream does not contain enough bits.");
        }

        uint result = 0;

        for (int i = 0; i < bitCount; i++)
        {
            int byteIndex = _positionBits >> 3;
            int bitIndex = 7 - (_positionBits & 7);

            uint bit = (uint)(
                (_data.Span[byteIndex] >> bitIndex) & 1);

            result = (result << 1) | bit;
            _positionBits++;
        }

        return result;
    }

    /// <summary>
    /// Reads bits without advancing the current position.
    /// </summary>
    public uint PeekBits(int bitCount)
    {
        int originalPosition = _positionBits;

        try
        {
            return ReadBits(bitCount);
        }
        finally
        {
            _positionBits = originalPosition;
        }
    }

    /// <summary>
    /// Reads a signed two's-complement integer.
    /// </summary>
    public int ReadSignedBits(int bitCount)
    {
        if (bitCount is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitCount),
                "A signed read must contain between 1 and 32 bits.");
        }

        uint value = ReadBits(bitCount);

        if (bitCount == 32)
        {
            return unchecked((int)value);
        }

        uint signMask = 1u << (bitCount - 1);

        if ((value & signMask) == 0)
        {
            return (int)value;
        }

        uint extensionMask = uint.MaxValue << bitCount;

        return unchecked((int)(value | extensionMask));
    }

    /// <summary>
    /// Advances the reader by the specified number of bits.
    /// </summary>
    public void SkipBits(int bitCount)
    {
        if (bitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        }

        if (bitCount > RemainingBits)
        {
            throw new EndOfStreamException(
                "The AAC bitstream does not contain enough bits to skip.");
        }

        _positionBits += bitCount;
    }

    /// <summary>
    /// Advances to the next byte boundary.
    /// </summary>
    public void AlignToByte()
    {
        int bitsToSkip = (8 - (_positionBits & 7)) & 7;
        SkipBits(bitsToSkip);
    }

    /// <summary>
    /// Reads one byte when the reader is byte-aligned.
    /// </summary>
    public byte ReadByte()
    {
        EnsureByteAligned();
        return (byte)ReadBits(8);
    }

    /// <summary>
    /// Reads the specified number of bytes when byte-aligned.
    /// </summary>
    public byte[] ReadBytes(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        EnsureByteAligned();

        int bitCount = checked(count * 8);

        if (bitCount > RemainingBits)
        {
            throw new EndOfStreamException(
                "The AAC bitstream does not contain enough bytes.");
        }

        int byteOffset = _positionBits >> 3;
        byte[] result = _data.Slice(byteOffset, count).ToArray();

        _positionBits += bitCount;

        return result;
    }

    private void EnsureByteAligned()
    {
        if (!IsByteAligned)
        {
            throw new InvalidOperationException(
                "The AAC bit reader must be byte-aligned for this operation.");
        }
    }

    private static void ValidateBitCount(int bitCount)
    {
        if (bitCount is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        }
    }
}
