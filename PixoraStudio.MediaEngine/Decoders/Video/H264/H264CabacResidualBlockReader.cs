namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Decodes the significance map and coefficient levels of one CABAC residual
/// block. Context offsets and scan-position increments are supplied by the
/// residual-block category and picture-level context derivation.
/// </summary>
public sealed class H264CabacResidualBlockReader
{
    /// <summary>
    /// Decodes coded_block_flag (where the category carries one), then the
    /// residual payload. contextIndexIncrement is condTermFlagA + 2*condTermFlagB.
    /// Category 0 has a fixed context and therefore requires increment zero.
    /// </summary>
    public PixoraStudio.MediaEngine.Models.Video.H264CavlcResidualBlock ReadBlockWithCodedBlockFlag(
        IH264CabacBinReader reader,
        H264CabacResidualBlockContextMap contextMap,
        int contextIndexIncrement)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(contextMap);

        if (contextMap.CodedBlockFlagContextOffset is not int contextOffset)
        {
            if (contextMap.ContextBlockCategory == 5)
                return ReadBlock(reader, contextMap, codedBlockFlag: true);
            throw new ArgumentException(
                "The supplied residual category does not define coded_block_flag.",
                nameof(contextMap));
        }

        int maximumIncrement = contextMap.ContextBlockCategory == 0 ? 0 : 3;
        if (contextIndexIncrement < 0 || contextIndexIncrement > maximumIncrement)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));

        int codedBlockFlag = reader.DecodeBin(contextOffset + contextIndexIncrement);
        if (codedBlockFlag is not (0 or 1))
            throw new FormatException("CABAC decision bins must be zero or one.");

        return ReadBlock(reader, contextMap, codedBlockFlag == 1);
    }

    /// <summary>
    /// Decodes a residual block using its normative category context map.
    /// For categories 0..4, codedBlockFlag=false represents an uncoded block
    /// and consumes no CABAC bins. Category 5 infers coded-block presence
    /// from the macroblock coded-block pattern and therefore cannot be false.
    /// </summary>
    public PixoraStudio.MediaEngine.Models.Video.H264CavlcResidualBlock ReadBlock(
        IH264CabacBinReader reader,
        H264CabacResidualBlockContextMap contextMap,
        bool codedBlockFlag = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(contextMap);

        if (!codedBlockFlag)
        {
            if (contextMap.HasImplicitCodedBlockFlag)
                throw new ArgumentException(
                    "The luma 8x8 category infers coded-block presence from coded_block_pattern.",
                    nameof(codedBlockFlag));

            return new PixoraStudio.MediaEngine.Models.Video.H264CavlcResidualBlock(
                contextMap.MaximumCoefficients, 0, 0, 0,
                new int[contextMap.MaximumCoefficients]);
        }

        int[] coefficients = ReadBlock(
            reader,
            contextMap.MaximumCoefficients,
            contextMap.StartIndex,
            contextMap.MaximumCoefficients - 1,
            contextMap.SignificantContextOffset,
            contextMap.LastSignificantContextOffset,
            contextMap.AbsoluteLevelContextOffset,
            contextMap.SignificantContextIncrements,
            contextMap.LastSignificantContextIncrements);

        int totalCoefficients = coefficients.Count(value => value != 0);
        return new PixoraStudio.MediaEngine.Models.Video.H264CavlcResidualBlock(
            contextMap.MaximumCoefficients,
            totalCoefficients,
            0,
            0,
            coefficients);
    }

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
