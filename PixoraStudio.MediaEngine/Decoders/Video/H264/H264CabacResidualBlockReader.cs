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
            {
                if (contextIndexIncrement != 0)
                    throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));
                return ReadBlock(reader, contextMap, codedBlockFlag: true);
            }
            throw new ArgumentException(
                "The supplied residual category does not define coded_block_flag.",
                nameof(contextMap));
        }

        int maximumIncrement = 3;
        if (contextIndexIncrement < 0 || contextIndexIncrement > maximumIncrement)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));

        int codedBlockFlagContext = contextOffset + contextIndexIncrement;
        H264CabacDiagnosticTrace.Mark(
            reader, $"residual coded_block_flag context={codedBlockFlagContext} begin");
        int codedBlockFlag = reader.DecodeBin(codedBlockFlagContext);
        H264CabacDiagnosticTrace.Mark(
            reader, $"residual coded_block_flag context={codedBlockFlagContext} value={codedBlockFlag}");
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
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual block category={contextMap.ContextBlockCategory} coded=False");
            if (contextMap.HasImplicitCodedBlockFlag)
                throw new ArgumentException(
                    "The luma 8x8 category infers coded-block presence from coded_block_pattern.",
                    nameof(codedBlockFlag));

            return new PixoraStudio.MediaEngine.Models.Video.H264CavlcResidualBlock(
                contextMap.MaximumCoefficients, 0, 0, 0,
                new int[contextMap.MaximumCoefficients]);
        }

        H264CabacDiagnosticTrace.Mark(
            reader,
            $"residual block category={contextMap.ContextBlockCategory} " +
                $"max_coefficients={contextMap.MaximumCoefficients} coded=True begin");
        int[] coefficients = ReadBlock(
            reader,
            contextMap.MaximumCoefficients,
            contextMap.StartIndex,
            contextMap.MaximumCoefficients - 1,
            contextMap.SignificantContextOffset,
            contextMap.LastSignificantContextOffset,
            contextMap.AbsoluteLevelContextOffset,
            contextMap.SignificantContextIncrements,
            contextMap.LastSignificantContextIncrements,
            contextMap.AbsoluteLevelContextAdjust);

        int totalCoefficients = coefficients.Count(value => value != 0);
        H264CabacDiagnosticTrace.Mark(
            reader,
            $"residual block category={contextMap.ContextBlockCategory} " +
                $"total_coefficients={totalCoefficients} complete");
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
            int significantContext = significantContextOffset +
                significantContextIncrements[mapIndex];
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual significant_coeff_flag position={position} " +
                    $"context={significantContext} begin");
            int significantBin = DecodeDecision(reader, significantContext);
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual significant_coeff_flag position={position} value={significantBin}");
            if (significantBin == 0)
                continue;

            significant[position] = true;
            int lastContext = lastSignificantContextOffset +
                lastSignificantContextIncrements[mapIndex];
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual last_significant_coeff_flag position={position} " +
                    $"context={lastContext} begin");
            int lastBin = DecodeDecision(reader, lastContext);
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual last_significant_coeff_flag position={position} value={lastBin}");
            if (lastBin == 1)
            {
                finalPosition = position;
                break;
            }
        }

        // If no last_significant_coeff_flag terminated the scan, the final
        // coefficient at endIndex is inferred to be significant.
        significant[finalPosition] = true;
        H264CabacDiagnosticTrace.Mark(
            reader, $"residual final_significant_position={finalPosition} " +
                $"inferred={(finalPosition == endIndex)}");

        // H.264 7.3.5.3.3 decodes each coefficient's magnitude immediately
        // followed by its bypass-coded sign, in reverse scan order. Keeping
        // these reads paired is essential: the sign bins are interleaved with
        // the next coefficient's magnitude bins in the CABAC stream.
        int levelContextState = 0;
        for (int position = finalPosition; position >= startIndex; position--)
        {
            if (!significant[position])
                continue;

            int stateBefore = levelContextState;
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual coefficient position={position} " +
                    $"abs_level_minus1 begin state={stateBefore}");
            int absoluteLevelMinusOne = ReadAbsoluteLevelMinusOne(
                reader, absoluteLevelContextOffset, ref levelContextState,
                absoluteLevelContextAdjust);

            int absoluteLevel = checked(absoluteLevelMinusOne + 1);
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual coefficient position={position} " +
                    $"abs_level_minus1={absoluteLevelMinusOne} level={absoluteLevel} " +
                    $"next_state={levelContextState}");
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual coefficient position={position} sign begin");
            int sign = reader.DecodeBypass();
            if (sign is not (0 or 1))
                throw new FormatException("CABAC bypass bins must be zero or one.");

            coefficients[position] = sign == 0 ? absoluteLevel : -absoluteLevel;
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual coefficient position={position} sign={sign} " +
                    $"value={coefficients[position]} complete");
        }

        return coefficients;
    }

    private static int ReadAbsoluteLevelMinusOne(
        IH264CabacBinReader reader,
        int contextOffset,
        ref int nodeContextState,
        int contextAdjust)
    {
        // H.264 CABAC residual coefficient-level context state machine. The node
        // state tracks decoded levels in reverse scan order, independently of
        // the residual category's absolute context offset.
        ReadOnlySpan<int> levelOneContextIncrements = [1, 2, 3, 4, 0, 0, 0, 0];
        ReadOnlySpan<int> levelGreaterThanOneContextIncrements =
            contextAdjust == 0
                ? [5, 5, 5, 5, 6, 7, 8, 9]
                : [5, 5, 5, 5, 6, 7, 8, 8];
        ReadOnlySpan<int> transitionAfterOne = [1, 2, 3, 3, 4, 5, 6, 7];
        ReadOnlySpan<int> transitionAfterGreaterThanOne = [4, 4, 4, 4, 5, 6, 7, 7];

        if ((uint)nodeContextState >= 8)
            throw new InvalidOperationException("CABAC coefficient-level context state is invalid.");

        int firstContext = contextOffset + levelOneContextIncrements[nodeContextState];
        H264CabacDiagnosticTrace.Mark(
            reader, $"residual abs_level_minus1 first_context={firstContext} " +
                $"state={nodeContextState} begin");
        int firstBin = DecodeDecision(reader, firstContext);
        H264CabacDiagnosticTrace.Mark(
            reader, $"residual abs_level_minus1 first_bin={firstBin} " +
                $"state={nodeContextState}");
        if (firstBin == 0)
        {
            nodeContextState = transitionAfterOne[nodeContextState];
            return 0;
        }

        int previousState = nodeContextState;
        int greaterThanOneContext = contextOffset +
            levelGreaterThanOneContextIncrements[previousState];
        nodeContextState = transitionAfterGreaterThanOne[previousState];
        int absoluteLevel = 2;

        while (absoluteLevel < 15)
        {
            int bin = DecodeDecision(reader, greaterThanOneContext);
            if (bin == 0)
                return absoluteLevel - 1;
            absoluteLevel++;
        }

        // Escape suffix: a bypass-coded unary prefix followed by a suffix
        // of the same length, then the category-independent base value 14.
        int escapeOrder = 0;
        while (escapeOrder < 23)
        {
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual abs_level_minus1 escape_prefix[{escapeOrder}] begin");
            int bin = reader.DecodeBypass();
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual abs_level_minus1 escape_prefix[{escapeOrder}]={bin}");
            if (bin is not (0 or 1))
                throw new FormatException("CABAC bypass bins must be zero or one.");
            if (bin == 0)
                break;
            escapeOrder++;
        }

        long escapedMagnitude = 1;
        for (int bit = 0; bit < escapeOrder; bit++)
        {
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual abs_level_minus1 escape_suffix[{bit}] begin");
            int bin = reader.DecodeBypass();
            H264CabacDiagnosticTrace.Mark(
                reader, $"residual abs_level_minus1 escape_suffix[{bit}]={bin}");
            if (bin is not (0 or 1))
                throw new FormatException("CABAC bypass bins must be zero or one.");
            escapedMagnitude = (escapedMagnitude << 1) | (uint)bin;
        }

        long level = 14 + escapedMagnitude;
        if (level > int.MaxValue)
            throw new FormatException("CABAC coefficient level exceeds Int32.");
        return (int)level - 1;
    }

    private static int DecodeDecision(IH264CabacBinReader reader, int contextIndex)
    {
        H264CabacDiagnosticTrace.Mark(
            reader, $"residual decision context={contextIndex} begin");
        int bin = reader.DecodeBin(contextIndex);
        H264CabacDiagnosticTrace.Mark(
            reader, $"residual decision context={contextIndex} value={bin}");
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
