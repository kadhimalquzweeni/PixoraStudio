namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Decodes the table-coded VLC syntax elements used by H.264 CAVLC.</summary>
public sealed class H264CavlcVlcDecoder
{
    private static readonly byte[][] coeff_token_len =
    [
        [1, 0, 0, 0, 6, 2, 0, 0, 8, 6, 3, 0, 9, 8, 7, 5, 10, 9, 8, 6, 11, 10, 9, 7, 13, 11, 10, 8, 13, 13, 11, 9, 13, 13, 13, 10, 14, 14, 13, 11, 14, 14, 14, 13, 15, 15, 14, 14, 15, 15, 15, 14, 16, 15, 15, 15, 16, 16, 16, 15, 16, 16, 16, 16, 16, 16, 16, 16],
        [2, 0, 0, 0, 6, 2, 0, 0, 6, 5, 3, 0, 7, 6, 6, 4, 8, 6, 6, 4, 8, 7, 7, 5, 9, 8, 8, 6, 11, 9, 9, 6, 11, 11, 11, 7, 12, 11, 11, 9, 12, 12, 12, 11, 12, 12, 12, 11, 13, 13, 13, 12, 13, 13, 13, 13, 13, 14, 13, 13, 14, 14, 14, 13, 14, 14, 14, 14],
        [4, 0, 0, 0, 6, 4, 0, 0, 6, 5, 4, 0, 6, 5, 5, 4, 7, 5, 5, 4, 7, 5, 5, 4, 7, 6, 6, 4, 7, 6, 6, 4, 8, 7, 7, 5, 8, 8, 7, 6, 9, 8, 8, 7, 9, 9, 8, 8, 9, 9, 9, 8, 10, 9, 9, 9, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10],
        [6, 0, 0, 0, 6, 6, 0, 0, 6, 6, 6, 0, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6]
    ];
    private static readonly byte[][] coeff_token_bits =
    [
        [1, 0, 0, 0, 5, 1, 0, 0, 7, 4, 1, 0, 7, 6, 5, 3, 7, 6, 5, 3, 7, 6, 5, 4, 15, 6, 5, 4, 11, 14, 5, 4, 8, 10, 13, 4, 15, 14, 9, 4, 11, 10, 13, 12, 15, 14, 9, 12, 11, 10, 13, 8, 15, 1, 9, 12, 11, 14, 13, 8, 7, 10, 9, 12, 4, 6, 5, 8],
        [3, 0, 0, 0, 11, 2, 0, 0, 7, 7, 3, 0, 7, 10, 9, 5, 7, 6, 5, 4, 4, 6, 5, 6, 7, 6, 5, 8, 15, 6, 5, 4, 11, 14, 13, 4, 15, 10, 9, 4, 11, 14, 13, 12, 8, 10, 9, 8, 15, 14, 13, 12, 11, 10, 9, 12, 7, 11, 6, 8, 9, 8, 10, 1, 7, 6, 5, 4],
        [15, 0, 0, 0, 15, 14, 0, 0, 11, 15, 13, 0, 8, 12, 14, 12, 15, 10, 11, 11, 11, 8, 9, 10, 9, 14, 13, 9, 8, 10, 9, 8, 15, 14, 13, 13, 11, 14, 10, 12, 15, 10, 13, 12, 11, 14, 9, 12, 8, 10, 13, 8, 13, 7, 9, 12, 9, 12, 11, 10, 5, 8, 7, 6, 1, 4, 3, 2],
        [3, 0, 0, 0, 0, 1, 0, 0, 4, 5, 6, 0, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63]
    ];
    private static readonly byte[][] chroma_dc_coeff_token_len =
    [

    ];
    private static readonly byte[][] chroma_dc_coeff_token_bits =
    [

    ];
    private static readonly byte[][] chroma422_dc_coeff_token_len =
    [

    ];
    private static readonly byte[][] chroma422_dc_coeff_token_bits =
    [

    ];
    private static readonly byte[][] total_zeros_len =
    [
        [1, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 9],
        [3, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 6, 6, 6, 6, 0],
        [4, 3, 3, 3, 4, 4, 3, 3, 4, 5, 5, 6, 5, 6, 0, 0],
        [5, 3, 4, 4, 3, 3, 3, 4, 3, 4, 5, 5, 5, 0, 0, 0],
        [4, 4, 4, 3, 3, 3, 3, 3, 4, 5, 4, 5, 0, 0, 0, 0],
        [6, 5, 3, 3, 3, 3, 3, 3, 4, 3, 6, 0, 0, 0, 0, 0],
        [6, 5, 3, 3, 3, 2, 3, 4, 3, 6, 0, 0, 0, 0, 0, 0],
        [6, 4, 5, 3, 2, 2, 3, 3, 6, 0, 0, 0, 0, 0, 0, 0],
        [6, 6, 4, 2, 2, 3, 2, 5, 0, 0, 0, 0, 0, 0, 0, 0],
        [5, 5, 3, 2, 2, 2, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [4, 4, 3, 3, 1, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [4, 4, 2, 1, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [3, 3, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [2, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
    ];
    private static readonly byte[][] total_zeros_bits =
    [
        [1, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 1],
        [7, 6, 5, 4, 3, 5, 4, 3, 2, 3, 2, 3, 2, 1, 0, 0],
        [5, 7, 6, 5, 4, 3, 4, 3, 2, 3, 2, 1, 1, 0, 0, 0],
        [3, 7, 5, 4, 6, 5, 4, 3, 3, 2, 2, 1, 0, 0, 0, 0],
        [5, 4, 3, 7, 6, 5, 4, 3, 2, 1, 1, 0, 0, 0, 0, 0],
        [1, 1, 7, 6, 5, 4, 3, 2, 1, 1, 0, 0, 0, 0, 0, 0],
        [1, 1, 5, 4, 3, 3, 2, 1, 1, 0, 0, 0, 0, 0, 0, 0],
        [1, 1, 1, 3, 3, 2, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0],
        [1, 0, 1, 3, 2, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0],
        [1, 0, 1, 3, 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0, 1, 1, 2, 1, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
    ];
    private static readonly byte[][] chroma_dc_total_zeros_len =
    [
        [1, 2, 3, 3],
        [1, 2, 2, 0],
        [1, 1, 0, 0]
    ];
    private static readonly byte[][] chroma_dc_total_zeros_bits =
    [
        [1, 1, 1, 0],
        [1, 1, 0, 0],
        [1, 0, 0, 0]
    ];
    private static readonly byte[][] chroma422_dc_total_zeros_len =
    [
        [1, 3, 3, 4, 4, 4, 5, 5],
        [3, 2, 3, 3, 3, 3, 3, 0],
        [3, 3, 2, 2, 3, 3, 0, 0],
        [3, 2, 2, 2, 3, 0, 0, 0],
        [2, 2, 2, 2, 0, 0, 0, 0],
        [2, 2, 1, 0, 0, 0, 0, 0],
        [1, 1, 0, 0, 0, 0, 0, 0]
    ];
    private static readonly byte[][] chroma422_dc_total_zeros_bits =
    [
        [1, 2, 3, 2, 3, 1, 1, 0],
        [0, 1, 1, 4, 5, 6, 7, 0],
        [0, 1, 1, 2, 6, 7, 0, 0],
        [6, 0, 1, 2, 7, 0, 0, 0],
        [0, 1, 2, 3, 0, 0, 0, 0],
        [0, 1, 1, 0, 0, 0, 0, 0],
        [0, 1, 0, 0, 0, 0, 0, 0]
    ];
    private static readonly byte[][] run_len =
    [
        [1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [1, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [2, 2, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [2, 2, 2, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [2, 2, 3, 3, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [2, 3, 3, 3, 3, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [3, 3, 3, 3, 3, 3, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0]
    ];
    private static readonly byte[][] run_bits =
    [
        [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [3, 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [3, 2, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [3, 0, 1, 3, 2, 5, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [7, 6, 5, 4, 3, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0]
    ];

    /// <summary>Decodes coeff_token. maxCoefficients 4/8 selects chroma DC; 15/16 selects luma/AC.</summary>
    public H264CoeffToken DecodeCoeffToken(H264BitReader reader, int predictedNonZeroCount, int maxCoefficients = 16)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ValidateMaxCoefficients(maxCoefficients);
        if (predictedNonZeroCount is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(predictedNonZeroCount));

        byte[] lengths;
        byte[] bits;
        if (maxCoefficients == 4)
        {
            lengths = Flatten(ChromaDcCoeffTokenLengths);
            bits = Flatten(ChromaDcCoeffTokenBits);
        }
        else if (maxCoefficients == 8)
        {
            lengths = Flatten(Chroma422DcCoeffTokenLengths);
            bits = Flatten(Chroma422DcCoeffTokenBits);
        }
        else
        {
            int table = predictedNonZeroCount < 2 ? 0
                : predictedNonZeroCount < 4 ? 1
                : predictedNonZeroCount < 8 ? 2 : 3;
            lengths = CoeffTokenLengths[table];
            bits = CoeffTokenBits[table];
        }

        int symbol = DecodeSymbol(reader, lengths, bits, lengths.Length);
        int total = symbol / 4;
        int trailing = symbol % 4;
        if (total > maxCoefficients || trailing > Math.Min(3, total))
            throw new FormatException("Decoded coeff_token is outside the permitted coefficient range.");
        return new H264CoeffToken(total, trailing);
    }

    /// <summary>Decodes total_zeros for 4x4, chroma DC 4:2:0, or chroma DC 4:2:2 blocks.</summary>
    public int DecodeTotalZeros(H264BitReader reader, int totalCoefficients, int maxCoefficients = 16)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ValidateMaxCoefficients(maxCoefficients);
        if (totalCoefficients < 1 || totalCoefficients >= maxCoefficients)
            throw new ArgumentOutOfRangeException(nameof(totalCoefficients));

        byte[] lengths;
        byte[] bits;
        if (maxCoefficients == 4)
        {
            lengths = ChromaDcTotalZerosLengths[totalCoefficients - 1];
            bits = ChromaDcTotalZerosBits[totalCoefficients - 1];
        }
        else if (maxCoefficients == 8)
        {
            lengths = Chroma422DcTotalZerosLengths[totalCoefficients - 1];
            bits = Chroma422DcTotalZerosBits[totalCoefficients - 1];
        }
        else
        {
            lengths = TotalZerosLengths[totalCoefficients - 1];
            bits = TotalZerosBits[totalCoefficients - 1];
        }

        int value = DecodeSymbol(reader, lengths, bits, maxCoefficients - totalCoefficients + 1);
        return value;
    }

    /// <summary>Decodes run_before using the table selected by zerosLeft.</summary>
    public int DecodeRunBefore(H264BitReader reader, int zerosLeft)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (zerosLeft is < 1 or > 15)
            throw new ArgumentOutOfRangeException(nameof(zerosLeft));

        int table = Math.Min(zerosLeft, 7) - 1;
        int value = DecodeSymbol(reader, RunLengths[table], RunBits[table],
            Math.Min(zerosLeft, table == 6 ? 14 : table + 1) + 1);
        return value;
    }

    private static int DecodeSymbol(H264BitReader reader, byte[] lengths, byte[] bits, int symbolCount)
    {
        int code = 0;
        int maxLength = lengths.Take(symbolCount).Max();
        for (int length = 1; length <= maxLength; length++)
        {
            if (reader.RemainingBits == 0)
                throw new EndOfStreamException("Truncated CAVLC VLC code.");
            code = (code << 1) | (int)reader.ReadBits(1);
            for (int symbol = 0; symbol < symbolCount; symbol++)
            {
                if (lengths[symbol] == length && bits[symbol] == code)
                    return symbol;
            }
        }
        throw new FormatException("Invalid CAVLC VLC code.");
    }

    private static byte[] Flatten(byte[][] rows) => rows.SelectMany(row => row).ToArray();

    private static void ValidateMaxCoefficients(int maxCoefficients)
    {
        if (maxCoefficients is not (4 or 8 or 15 or 16))
            throw new ArgumentOutOfRangeException(nameof(maxCoefficients));
    }
}

public sealed record H264CoeffToken(int TotalCoefficients, int TrailingOnes);
