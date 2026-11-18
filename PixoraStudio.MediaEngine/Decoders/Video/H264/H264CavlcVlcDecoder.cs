using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Decodes the table-coded VLC syntax elements used by H.264 CAVLC.</summary>
public sealed class H264CavlcVlcDecoder
{
    private static readonly byte[][] CoeffTokenLengths =
    [

    ];
    private static readonly byte[][] CoeffTokenBits =
    [

    ];
    private static readonly byte[][] ChromaDcCoeffTokenLengths =
    [

    ];
    private static readonly byte[][] ChromaDcCoeffTokenBits =
    [

    ];
    private static readonly byte[][] Chroma422DcCoeffTokenLengths =
    [

    ];
    private static readonly byte[][] Chroma422DcCoeffTokenBits =
    [

    ];
    private static readonly byte[][] TotalZerosLengths =
    [

    ];
    private static readonly byte[][] TotalZerosBits =
    [

    ];
    private static readonly byte[][] ChromaDcTotalZerosLengths =
    [

    ];
    private static readonly byte[][] ChromaDcTotalZerosBits =
    [

    ];
    private static readonly byte[][] Chroma422DcTotalZerosLengths =
    [

    ];
    private static readonly byte[][] Chroma422DcTotalZerosBits =
    [

    ];
    private static readonly byte[][] RunLengths =
    [

    ];
    private static readonly byte[][] RunBits =
    [

    ];

    /// <summary>Decodes coeff_token. Supply maxCoefficients=4 or 8 for chroma DC, otherwise 15 or 16.</summary>
    public H264CoeffToken DecodeCoeffToken(H264BitReader reader, int predictedNonZeroCount, int maxCoefficients = 16)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (maxCoefficients is not (4 or 8 or 15 or 16))
            throw new ArgumentOutOfRangeException(nameof(maxCoefficients));
        if (predictedNonZeroCount is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(predictedNonZeroCount));

        byte[] lengths;
        byte[] bits;
        int entriesPerTotal;
        if (maxCoefficients == 4)
        {
            lengths = Flatten(ChromaDcCoeffTokenLengths);
            bits = Flatten(ChromaDcCoeffTokenBits);
            entriesPerTotal = 4;
        }
        else if (maxCoefficients == 8)
        {
            lengths = Flatten(Chroma422DcCoeffTokenLengths);
            bits = Flatten(Chroma422DcCoeffTokenBits);
            entriesPerTotal = 4;
        }
        else
        {
            int table = predictedNonZeroCount < 2 ? 0
                : predictedNonZeroCount < 4 ? 1
                : predictedNonZeroCount < 8 ? 2 : 3;
            lengths = CoeffTokenLengths[table];
            bits = CoeffTokenBits[table];
            entriesPerTotal = 4;
        }

        int totalLimit = Math.Min(maxCoefficients, lengths.Length / entriesPerTotal - 1);
        for (int total = 0; total <= totalLimit; total++)
        {
            for (int trailing = 0; trailing <= Math.Min(3, total); trailing++)
            {
                int index = total * entriesPerTotal + trailing;
                int length = lengths[index];
                if (length == 0) continue;
                if (TryReadCode(reader, length, bits[index]))
                    return new H264CoeffToken(total, trailing);
            }
        }

        throw new FormatException("Invalid coeff_token VLC code.");
    }

    /// <summary>Decodes total_zeros for 4x4, chroma DC 4:2:0, or chroma DC 4:2:2 blocks.</summary>
    public int DecodeTotalZeros(H264BitReader reader, int totalCoefficients, int maxCoefficients = 16)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (maxCoefficients is not (4 or 8 or 15 or 16))
            throw new ArgumentOutOfRangeException(nameof(maxCoefficients));
        if (totalCoefficients < 1 || totalCoefficients >= maxCoefficients)
            throw new ArgumentOutOfRangeException(nameof(totalCoefficients));

        byte[] lengths;
        byte[] bits;
        int row;
        int valueLimit;
        if (maxCoefficients == 4)
        {
            lengths = ChromaDcTotalZerosLengths[totalCoefficients - 1];
            bits = ChromaDcTotalZerosBits[totalCoefficients - 1];
            row = 0;
            valueLimit = maxCoefficients - totalCoefficients;
        }
        else if (maxCoefficients == 8)
        {
            lengths = Chroma422DcTotalZerosLengths[totalCoefficients - 1];
            bits = Chroma422DcTotalZerosBits[totalCoefficients - 1];
            row = 0;
            valueLimit = maxCoefficients - totalCoefficients;
        }
        else
        {
            lengths = TotalZerosLengths[totalCoefficients - 1];
            bits = TotalZerosBits[totalCoefficients - 1];
            row = 0;
            valueLimit = maxCoefficients - totalCoefficients;
        }

        for (int value = 0; value <= valueLimit; value++)
        {
            int length = lengths[row + value];
            if (length > 0 && TryReadCode(reader, length, bits[row + value]))
                return value;
        }
        throw new FormatException("Invalid total_zeros VLC code.");
    }

    /// <summary>Decodes run_before using the table selected by zerosLeft.</summary>
    public int DecodeRunBefore(H264BitReader reader, int zerosLeft)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (zerosLeft < 1 || zerosLeft > 15)
            throw new ArgumentOutOfRangeException(nameof(zerosLeft));

        int table = Math.Min(zerosLeft, 7) - 1;
        byte[] lengths = RunLengths[table];
        byte[] bits = RunBits[table];
        int limit = Math.Min(zerosLeft, table == 6 ? 14 : table + 1);
        for (int run = 0; run <= limit; run++)
        {
            int length = lengths[run];
            if (length > 0 && TryReadCode(reader, length, bits[run]))
                return run;
        }
        throw new FormatException("Invalid run_before VLC code.");
    }

    private static byte[] Flatten(byte[][] rows) => rows.SelectMany(row => row).ToArray();

    private static bool TryReadCode(H264BitReader reader, int length, int expected)
    {
        if (reader.RemainingBits < length)
            return false;

        uint actual = reader.ReadBits(length);
        if (actual == (uint)expected)
            return true;

        // A mismatching prefix is not a valid candidate; rewind by using a local reader is not possible.
        // The caller therefore uses the prefix-trie method below instead.
        throw new InvalidOperationException("Internal VLC candidate probing must not consume mismatching codes.");
    }
}

public sealed record H264CoeffToken(int TotalCoefficients, int TrailingOnes);
