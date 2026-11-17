using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reconstructs quantised spectral tuples from AAC Huffman indices,
/// including unsigned sign bits and codebook 11 escape values.
/// </summary>
public sealed class AacSpectralTupleDecoder
{
    public const int MaximumQuantisedMagnitude = 8191;

    /// <summary>
    /// Reconstructs a tuple from its decoded Huffman index.
    /// </summary>
    public int[] DecodeTupleFromIndex(
        int codebookIndex,
        int huffmanIndex)
    {
        AacSpectralCodebookInfo info =
            AacSpectralCodebookInfo.Get(codebookIndex);

        if (codebookIndex == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(codebookIndex),
                "Zero codebook does not contain Huffman tuples.");
        }

        int radix = info.MaximumMagnitude * 2 + 1;

        if (codebookIndex is 1 or 2 or 5 or 6)
        {
            radix = codebookIndex is 1 or 2 ? 3 : 9;
        }
        else
        {
            radix = info.MaximumMagnitude + 1;
        }

        int entryCount = 1;

        for (int i = 0; i < info.TupleSize; i++)
        {
            entryCount = checked(entryCount * radix);
        }

        if ((uint)huffmanIndex >= (uint)entryCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(huffmanIndex),
                huffmanIndex,
                "The Huffman index is outside the selected codebook.");
        }

        int[] tuple = new int[info.TupleSize];

        switch (codebookIndex)
        {
            case 1:
            case 2:
                DecodeSignedQuadruple(huffmanIndex, tuple);
                break;

            case 3:
            case 4:
                DecodeUnsignedQuadruple(huffmanIndex, tuple);
                break;

            case 5:
            case 6:
                DecodeSignedPair(huffmanIndex, tuple);
                break;

            case 7:
            case 8:
            case 9:
            case 10:
            case 11:
                DecodeUnsignedPair(
                    huffmanIndex,
                    info.MaximumMagnitude + 1,
                    tuple);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(codebookIndex));
        }

        return tuple;
    }

    /// <summary>
    /// Reads the sign bits and any escape values following
    /// an unsigned spectral Huffman codeword.
    /// </summary>
    public int[] DecodeTuple(
        AacBitReader bitReader,
        int codebookIndex,
        int huffmanIndex)
    {
        ArgumentNullException.ThrowIfNull(bitReader);

        int[] tuple = DecodeTupleFromIndex(
            codebookIndex,
            huffmanIndex);

        AacSpectralCodebookInfo info =
            AacSpectralCodebookInfo.Get(codebookIndex);

        if (info.IsSigned)
        {
            return tuple;
        }

        for (int i = 0; i < tuple.Length; i++)
        {
            int magnitude = tuple[i];

            if (magnitude == 0)
            {
                continue;
            }

            bool isNegative = bitReader.ReadBits(1) != 0;

            if (info.UsesEscapeValues && magnitude == 16)
            {
                magnitude = DecodeEscapeValue(bitReader);
            }

            tuple[i] = isNegative ? -magnitude : magnitude;
        }

        return tuple;
    }

    /// <summary>
    /// Decodes the AAC codebook 11 escape sequence.
    /// </summary>
    public int DecodeEscapeValue(AacBitReader bitReader)
    {
        ArgumentNullException.ThrowIfNull(bitReader);

        int escapeLength = 4;

        while (bitReader.ReadBits(1) != 0)
        {
            escapeLength++;

            if (escapeLength > 12)
            {
                throw new InvalidDataException(
                    "AAC escape magnitude exceeds the supported limit.");
            }
        }

        uint escapeWord = bitReader.ReadBits(escapeLength);

        int magnitude = checked(
            (1 << escapeLength) + (int)escapeWord);

        if (magnitude > MaximumQuantisedMagnitude)
        {
            throw new InvalidDataException(
                "AAC escape magnitude exceeds the maximum quantised value.");
        }

        return magnitude;
    }

    private static void DecodeSignedQuadruple(
        int index,
        int[] tuple)
    {
        tuple[0] = index / 27 - 1;
        tuple[1] = index / 9 % 3 - 1;
        tuple[2] = index / 3 % 3 - 1;
        tuple[3] = index % 3 - 1;
    }

    private static void DecodeUnsignedQuadruple(
        int index,
        int[] tuple)
    {
        tuple[0] = index / 27;
        tuple[1] = index / 9 % 3;
        tuple[2] = index / 3 % 3;
        tuple[3] = index % 3;
    }

    private static void DecodeSignedPair(
        int index,
        int[] tuple)
    {
        // AAC pair indices are encoded as 9 * (y + 4) + (x + 4).
        // The spectral tuple is ordered (x, y), so the remainder is first.
        tuple[0] = index % 9 - 4;
        tuple[1] = index / 9 - 4;
    }

    private static void DecodeUnsignedPair(
        int index,
        int radix,
        int[] tuple)
    {
        // AAC pair indices are encoded as radix * y + x.
        // The spectral tuple is ordered (x, y), so the remainder is first.
        tuple[0] = index % radix;
        tuple[1] = index / radix;
    }
}