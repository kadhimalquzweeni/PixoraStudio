namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Decodes the significance map and coefficient levels of one CABAC residual
/// block. Context offsets and scan-position increments are supplied by the
/// residual-block category and picture-level context derivation.
/// </summary>
public sealed class H264CabacResidualBlockReader
{
    public int[] ReadBlock(
        IH264CabacBinReader reader,
        int maximumCoefficients,
        int startIndex,
        int endIndex,
        int significantContextOffset,
        int lastSignificantContextOffset,
        int absoluteLevelContextOffset,
        IReadOnlyList<int> significantContextIncrements,
        IReadOnlyList<int> lastSignificantContextIncrements,
        int absoluteLevelContextAdjust = 0)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(significantContextIncrements);
        ArgumentNullException.ThrowIfNull(lastSignificantContextIncrements);

        if (maximumCoefficients <= 0 || maximumCoefficients > 64)
            throw new ArgumentOutOfRangeException(nameof(maximumCoefficients));
        if (startIndex < 0 || endIndex < startIndex || endIndex >= maximumCoefficients)
            throw new ArgumentOutOfRangeException(nameof(endIndex));
        if (significantContextOffset < 0 || lastSignificantContextOffset < 0 ||
            absoluteLevelContextOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(significantContextOffset));
        if (absoluteLevelContextAdjust is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(absoluteLevelContextAdjust));

        int positionsBeforeImpliedLast = endIndex - startIndex;
        if (significantContextIncrements.Count != positionsBeforeImpliedLast ||
            lastSignificantContextIncrements.Count != positionsBeforeImpliedLast)
            throw new ArgumentException(
                "Significance context maps must contain one entry for each position before the implied final coefficient.");
        ValidateContextIncrements(significantContextIncrements, nameof(significantContextIncrements));
        ValidateContextIncrements(lastSignificantContextIncrements, nameof(lastSignificantContextIncrements));

        int[] coefficients = new int[maximumCoefficients];
        bool[] significant = new bool[maximumCoefficients];
        int finalPosition = endIndex;

        for (int position = startIndex; position < endIndex; position++)
        {
            int mapIndex = position - startIndex;
            int significantBin = DecodeDecision(
                reader, significantContextOffset + significantContextIncrements[mapIndex]);
            if (significantBin == 0)
                continue;

            significant[position] = true;
            int lastBin = DecodeDecision(
                reader, lastSignificantContextOffset + lastSignificantContextIncrements[mapIndex]);
            if (lastBin == 1)
            {
                finalPosition = position;
                break;
            }
        }

        // If no last_significant_coeff_flag terminated the scan, the final
        // coefficient at endIndex is inferred to be significant.
        significant[finalPosition] = true;

        int decodedGreaterThanOne = 0;
        int decodedEqualToOne = 0;
        for (int position = finalPosition; position >= startIndex; position--)
        {
            if (!significant[position])
                continue;

            int absoluteLevelMinusOne = ReadAbsoluteLevelMinusOne(
                reader,
                absoluteLevelContextOffset,
                decodedGreaterThanOne,
                decodedEqualToOne,
                absoluteLevelContextAdjust);

            int absoluteLevel = checked(absoluteLevelMinusOne + 1);
            int sign = reader.DecodeBypass();
            if (sign is not (0 or 1))
                throw new FormatException("CABAC bypass bins must be zero or one.");

            coefficients[position] = sign == 0 ? absoluteLevel : -absoluteLevel;
            if (absoluteLevel == 1)
                decodedEqualToOne++;
            else
                decodedGreaterThanOne++;
        }

        return coefficients;
    }

    private static int ReadAbsoluteLevelMinusOne(
        IH264CabacBinReader reader,
        int contextOffset,
        int decodedGreaterThanOne,
        int decodedEqualToOne,
        int contextAdjust)
    {
        int firstIncrement = decodedGreaterThanOne != 0
            ? 0
            : Math.Min(4, 1 + decodedEqualToOne);
        int remainingIncrement = 5 +
            Math.Min(4 - contextAdjust, decodedGreaterThanOne);

        int bin = DecodeDecision(reader, contextOffset + firstIncrement);
        if (bin == 0)
            return 0;

        int value = 0;
        while (value < 13)
        {
            bin = DecodeDecision(reader, contextOffset + remainingIncrement);
            if (bin == 0)
                break;
            value++;
        }

        value += bin;
        if (value != 14)
            return value;

        // Escape suffix: unary-coded order followed by that many suffix bits.
        int escapeOrder = 0;
        while (true)
        {
            bin = reader.DecodeBypass();
            if (bin is not (0 or 1))
                throw new FormatException("CABAC bypass bins must be zero or one.");
            if (bin == 0)
                break;
            escapeOrder++;
            if (escapeOrder > 30)
                throw new FormatException("CABAC coefficient escape prefix exceeds Int32.");
        }

        long suffix = 0;
        for (int bit = escapeOrder - 1; bit >= 0; bit--)
        {
            bin = reader.DecodeBypass();
            if (bin is not (0 or 1))
                throw new FormatException("CABAC bypass bins must be zero or one.");
            suffix |= (long)bin << bit;
        }

        long escapedValue = 14L + ((1L << escapeOrder) - 1) + suffix;
        if (escapedValue > int.MaxValue)
            throw new FormatException("CABAC coefficient level exceeds Int32.");
        return (int)escapedValue;
    }

    private static int DecodeDecision(IH264CabacBinReader reader, int contextIndex)
    {
        int bin = reader.DecodeBin(contextIndex);
        return bin switch
        {
            0 or 1 => bin,
            _ => throw new FormatException("CABAC decision bins must be zero or one.")
        };
    }

    private static void ValidateContextIncrements(
        IReadOnlyList<int> increments,
        string parameterName)
    {
        for (int i = 0; i < increments.Count; i++)
        {
            if (increments[i] < 0)
                throw new ArgumentOutOfRangeException(parameterName,
                    "CABAC context increments cannot be negative.");
        }
    }
}
