using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Decodes CABAC-coded macroblock syntax elements.</summary>
public sealed class H264CabacSyntaxElementReader
{
    private readonly IH264CabacBinReader _reader;

    public H264CabacSyntaxElementReader(IH264CabacBinReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>The underlying bin reader, exposed for optional diagnostic annotations.</summary>
    internal IH264CabacBinReader BinReader => _reader;

    /// <summary>
    /// Decodes mb_type for an I slice: I_NxN (0), I16x16 (1..24), or
    /// I_PCM (25). The caller supplies the neighbour-derived first-bin
    /// context increment from H.264 clause 9.3.3.1.2.
    /// </summary>
    public int ReadIntraSliceMacroblockType(int firstBinContextIncrement) =>
        ReadIntraMacroblockType(3, firstBinContextIncrement, isIntraSlice: true);

    /// <summary>
    /// Decodes the intra macroblock type suffix used inside P and B slices.
    /// The returned value is the I-slice-relative code (0..25); callers add
    /// the P/B inter-type offset (5 for P, 23 for B).
    /// </summary>
    public int ReadInterSliceIntraMacroblockType(int contextBase) =>
        ReadIntraMacroblockType(contextBase, 0, isIntraSlice: false);

    private int ReadIntraMacroblockType(
        int contextBase,
        int firstBinContextIncrement,
        bool isIntraSlice)
    {
        if (contextBase < 0 || contextBase > 459)
            throw new ArgumentOutOfRangeException(nameof(contextBase));
        if (isIntraSlice && firstBinContextIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(firstBinContextIncrement));
        if (!isIntraSlice && firstBinContextIncrement != 0)
            throw new ArgumentOutOfRangeException(nameof(firstBinContextIncrement));

        int firstContext = contextBase + firstBinContextIncrement;
        if (!ReadFlagAtContext(firstContext))
            return 0;

        int pcmFlag = _reader.DecodeTerminate();
        if (pcmFlag is not (0 or 1))
            throw new FormatException("CABAC terminating bins must be zero or one.");
        if (pcmFlag == 1)
            return 25;

        // H.264 Table 9-36 uses different suffix context offsets for I
        // slices versus the intra_mb_type suffix in P/B slices. In P/B,
        // chroma_cbp_flag and its value share one context, as do both
        // intra16x16 prediction-mode bins.
        int lumaCbpContext = contextBase + (isIntraSlice ? 3 : 1);
        int chromaCbpContext = contextBase + (isIntraSlice ? 4 : 2);
        int chromaValueContext = contextBase + (isIntraSlice ? 5 : 2);
        int predictionModeContext = contextBase + (isIntraSlice ? 6 : 3);

        int lumaCbpFlag = ReadFlagAtContext(lumaCbpContext) ? 1 : 0;
        int chromaCbpFlag = ReadFlagAtContext(chromaCbpContext) ? 1 : 0;
        int chromaCbpValue = 0;
        if (chromaCbpFlag == 1)
            chromaCbpValue = ReadFlagAtContext(chromaValueContext) ? 1 : 0;

        int predictionModeHigh = ReadFlagAtContext(predictionModeContext) ? 1 : 0;
        int predictionModeLow = ReadFlagAtContext(
            predictionModeContext + (isIntraSlice ? 1 : 0)) ? 1 : 0;

        return 1 + (12 * lumaCbpFlag) +
            (chromaCbpFlag == 1 ? 4 + (4 * chromaCbpValue) : 0) +
            (2 * predictionModeHigh) + predictionModeLow;
    }

    /// <summary>
    /// Decodes mb_skip_flag for P or B slices. The context increment is the
    /// sum of the left and upper neighbour condTermFlag values.
    /// </summary>
    public bool ReadMacroblockSkipFlag(bool isBSlice, int contextIndexIncrement)
    {
        if (contextIndexIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));

        int contextIndex = (isBSlice ? 24 : 11) + contextIndexIncrement;
        string syntaxName = isBSlice ? "mb_skip_flag_b" : "mb_skip_flag_p";
        (int rangeBefore, int offsetBefore, int bitBefore) = GetDiagnosticState();
        int bin = _reader.DecodeBin(contextIndex);
        (int rangeAfter, int offsetAfter, int bitAfter) = GetDiagnosticState();
        H264CabacDiagnosticTrace.Mark(
            _reader,
            $"{syntaxName} ctx={contextIndex} inc={contextIndexIncrement} bin={bin} " +
            $"range={rangeBefore}->{rangeAfter} offset={offsetBefore}->{offsetAfter} " +
            $"bit={bitBefore}->{bitAfter}");
        if (bin is not (0 or 1))
            throw new FormatException("CABAC decision bins must be zero or one.");
        return bin == 1;
    }

    /// <summary>
    /// Decodes ref_idx_l0 or ref_idx_l1 as truncated unary. The caller supplies
    /// the neighbour-derived context increment and the largest permitted index.
    /// </summary>
    public int ReadReferenceIndex(int contextIndexIncrement, int maximumReferenceIndex)
    {
        if (contextIndexIncrement is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));
        if (maximumReferenceIndex is < 0 or > 31)
            throw new ArgumentOutOfRangeException(nameof(maximumReferenceIndex));

        int contextIncrement = contextIndexIncrement;
        for (int binIndex = 0; binIndex < maximumReferenceIndex; binIndex++)
        {
            int bin = _reader.DecodeBin(54 + contextIncrement);
            if (bin == 0)
                return binIndex;
            if (bin != 1)
                throw new FormatException("CABAC decision bins must be zero or one.");

            contextIncrement = (contextIncrement >> 2) + 4;
        }

        return maximumReferenceIndex;
    }

    /// <summary>Decodes end_of_slice_flag using the CABAC terminating-bin process.</summary>
    public bool ReadEndOfSliceFlag()
    {
        (int rangeBefore, int offsetBefore, int bitBefore) = GetDiagnosticState();
        int bin = _reader.DecodeTerminate();
        (int rangeAfter, int offsetAfter, int bitAfter) = GetDiagnosticState();
        H264CabacDiagnosticTrace.Mark(
            _reader,
            $"end_of_slice_flag bin={bin} range={rangeBefore}->{rangeAfter} " +
            $"offset={offsetBefore}->{offsetAfter} bit={bitBefore}->{bitAfter}");
        if (bin == 0)
            return false;
        if (bin == 1)
        {
            _reader.MarkTerminated();
            return true;
        }
        throw new FormatException("CABAC terminating bins must be zero or one.");
    }

    private (int Range, int Offset, int BitPosition) GetDiagnosticState() =>
        _reader is IH264CabacDiagnosticStateProvider state
            ? (state.ArithmeticRange, state.ArithmeticOffset, state.BitPosition)
            : (-1, -1, -1);

    /// <summary>
    /// Decodes CABAC coded_block_pattern for an intra macroblock. Each context
    /// increment is derived by the caller from the relevant neighbouring
    /// macroblock/block state (Table 9-39).
    /// </summary>
    public H264CodedBlockPattern ReadIntraCodedBlockPattern(
        IReadOnlyList<int> lumaContextIncrements,
        IReadOnlyList<int>? chromaContextIncrements,
        uint chromaArrayType)
    {
        ArgumentNullException.ThrowIfNull(lumaContextIncrements);
        if (chromaArrayType > 3)
            throw new ArgumentOutOfRangeException(nameof(chromaArrayType));
        if (lumaContextIncrements.Count != 4 ||
            lumaContextIncrements.Any(value => value is < 0 or > 3))
            throw new ArgumentException(
                "Four luma coded-block context increments in the range 0..3 are required.",
                nameof(lumaContextIncrements));
        if (chromaArrayType is 1 or 2)
        {
            ArgumentNullException.ThrowIfNull(chromaContextIncrements);
            if (chromaContextIncrements.Count != 2 ||
                chromaContextIncrements.Any(value => value is < 0 or > 3))
                throw new ArgumentException(
                    "Two chroma coded-block context increments in the range 0..3 are required.",
                    nameof(chromaContextIncrements));
        }

        uint lumaPattern = 0;
        for (int binIndex = 0; binIndex < 4; binIndex++)
        {
            int bin = _reader.DecodeBin(73 + lumaContextIncrements[binIndex]);
            if (bin is not (0 or 1))
                throw new FormatException("CABAC decision bins must be zero or one.");
            lumaPattern |= (uint)bin << binIndex;
        }

        uint chromaPattern = 0;
        if (chromaArrayType is 1 or 2)
        {
            int firstBin = _reader.DecodeBin(77 + chromaContextIncrements![0]);
            if (firstBin is not (0 or 1))
                throw new FormatException("CABAC decision bins must be zero or one.");
            if (firstBin == 1)
            {
                int secondBin = _reader.DecodeBin(81 + chromaContextIncrements![1]);
                if (secondBin is not (0 or 1))
                    throw new FormatException("CABAC decision bins must be zero or one.");
                chromaPattern = secondBin == 0 ? 1u : 2u;
            }
        }

        uint combinedPattern = lumaPattern | (chromaPattern << 4);
        return new H264CodedBlockPattern(
            combinedPattern, lumaPattern, chromaPattern, chromaArrayType);
    }


    /// <summary>
    /// Decodes intra coded_block_pattern while deriving each luma context
    /// increment after the preceding CBP bins have been decoded.
    /// </summary>
    public H264CodedBlockPattern ReadIntraCodedBlockPattern(
        Func<int, uint, int> lumaContextIncrementProvider,
        IReadOnlyList<int>? chromaContextIncrements,
        uint chromaArrayType)
    {
        ArgumentNullException.ThrowIfNull(lumaContextIncrementProvider);
        if (chromaArrayType > 3)
            throw new ArgumentOutOfRangeException(nameof(chromaArrayType));
        if (chromaArrayType is 1 or 2)
        {
            ArgumentNullException.ThrowIfNull(chromaContextIncrements);
            if (chromaContextIncrements.Count != 2 ||
                chromaContextIncrements.Any(value => value is < 0 or > 3))
                throw new ArgumentException(
                    "Two chroma coded-block context increments in the range 0..3 are required.",
                    nameof(chromaContextIncrements));
        }

        uint lumaPattern = 0;
        for (int binIndex = 0; binIndex < 4; binIndex++)
        {
            int increment = lumaContextIncrementProvider(binIndex, lumaPattern);
            if (increment is < 0 or > 3)
                throw new ArgumentOutOfRangeException(nameof(lumaContextIncrementProvider),
                    "Luma coded-block context increments must be in the range 0..3.");

            int bin = _reader.DecodeBin(73 + increment);
            if (bin is not (0 or 1))
                throw new FormatException("CABAC decision bins must be zero or one.");
            lumaPattern |= (uint)bin << binIndex;
        }

        uint chromaPattern = 0;
        if (chromaArrayType is 1 or 2)
        {
            int firstBin = _reader.DecodeBin(77 + chromaContextIncrements![0]);
            if (firstBin is not (0 or 1))
                throw new FormatException("CABAC decision bins must be zero or one.");
            if (firstBin == 1)
            {
                int secondBin = _reader.DecodeBin(81 + chromaContextIncrements![1]);
                if (secondBin is not (0 or 1))
                    throw new FormatException("CABAC decision bins must be zero or one.");
                chromaPattern = secondBin == 0 ? 1u : 2u;
            }
        }

        uint combinedPattern = lumaPattern | (chromaPattern << 4);
        return new H264CodedBlockPattern(
            combinedPattern, lumaPattern, chromaPattern, chromaArrayType);
    }

    /// <summary>Decodes prev_intra4x4_pred_mode_flag or prev_intra8x8_pred_mode_flag (ctxIdx 68).</summary>
    public bool ReadPreviousIntraPredictionModeFlag() =>
        ReadFlagAtContext(68);

    /// <summary>
    /// Decodes rem_intra4x4_pred_mode or rem_intra8x8_pred_mode as three
    /// context-coded bins, all using ctxIdx 69.
    /// </summary>
    public int ReadRemainingIntraPredictionMode()
    {
        int value = 0;
        for (int i = 0; i < 3; i++)
        {
            int bin = ReadFlagAtContext(69) ? 1 : 0;
            value |= bin << i;
        }
        return value;
    }

    /// <summary>Decodes transform_size_8x8_flag (ctxIdx 399).</summary>
    public bool ReadTransformSize8x8Flag(int contextIndexIncrement = 0)
    {
        if (contextIndexIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));
        return ReadFlagAtContext(399 + contextIndexIncrement);
    }

    /// <summary>
    /// Decodes intra_chroma_pred_mode as truncated unary (ctxIdx offset 64).
    /// The caller supplies the neighbouring-mode context increment.
    /// </summary>
    public int ReadIntraChromaPredictionMode(int contextIndexIncrement)
    {
        if (contextIndexIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));

        int firstBin = _reader.DecodeBin(64 + contextIndexIncrement);
        if (firstBin is not (0 or 1))
            throw new FormatException("CABAC decision bins must be zero or one.");
        if (firstBin == 0)
            return 0;

        int secondBin = _reader.DecodeBin(67);
        if (secondBin is not (0 or 1))
            throw new FormatException("CABAC decision bins must be zero or one.");
        if (secondBin == 0)
            return 1;

        int thirdBin = _reader.DecodeBin(67);
        if (thirdBin is not (0 or 1))
            throw new FormatException("CABAC decision bins must be zero or one.");
        return thirdBin == 0 ? 2 : 3;
    }


    /// <summary>
    /// Decodes one signed horizontal or vertical mvd component. The context
    /// increment is derived from the sum of absolute left and top MVD values.
    /// </summary>
    public int ReadMotionVectorDifference(int contextBase, int contextIndexIncrement)
    {
        if (contextBase is not (40 or 47))
            throw new ArgumentOutOfRangeException(nameof(contextBase));
        if (contextIndexIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));

        int firstContext = contextBase + contextIndexIncrement;
        H264CabacDiagnosticTrace.Mark(
            _reader, $"mvd syntax base={contextBase} increment={contextIndexIncrement} " +
                $"first_context={firstContext} begin");
        int firstBin = _reader.DecodeBin(firstContext);
        H264CabacDiagnosticTrace.Mark(
            _reader, $"mvd syntax first_bin context={firstContext} value={firstBin}");
        if (firstBin is not (0 or 1))
            throw new FormatException("CABAC decision bins must be zero or one.");
        if (firstBin == 0)
        {
            H264CabacDiagnosticTrace.Mark(_reader, "mvd syntax complete value=0");
            return 0;
        }

        int magnitude = 1;
        int contextIndex = contextBase + 3;
        int unaryIndex = 0;
        while (magnitude < 9)
        {
            H264CabacDiagnosticTrace.Mark(
                _reader, $"mvd syntax unary[{unaryIndex}] context={contextIndex} begin");
            int bin = _reader.DecodeBin(contextIndex);
            H264CabacDiagnosticTrace.Mark(
                _reader, $"mvd syntax unary[{unaryIndex}]={bin}");
            if (bin is not (0 or 1))
                throw new FormatException("CABAC decision bins must be zero or one.");
            if (bin == 0)
                break;

            if (magnitude < 4)
                contextIndex++;
            magnitude++;
            unaryIndex++;
        }

        if (magnitude == 9)
        {
            int order = 3;
            int escapePrefixIndex = 0;
            while (true)
            {
                H264CabacDiagnosticTrace.Mark(
                    _reader, $"mvd syntax escape_prefix[{escapePrefixIndex}] begin order={order}");
                int bin = _reader.DecodeBypass();
                H264CabacDiagnosticTrace.Mark(
                    _reader, $"mvd syntax escape_prefix[{escapePrefixIndex}]={bin}");
                if (bin is not (0 or 1))
                    throw new FormatException("CABAC bypass bins must be zero or one.");
                if (bin == 0)
                    break;

                magnitude = checked(magnitude + (1 << order));
                order++;
                escapePrefixIndex++;
                if (order > 24)
                    throw new FormatException("CABAC MVD escape prefix exceeds the supported range.");
            }

            for (int bit = order - 1; bit >= 0; bit--)
            {
                H264CabacDiagnosticTrace.Mark(
                    _reader, $"mvd syntax escape_suffix bit={bit} begin");
                int bin = _reader.DecodeBypass();
                H264CabacDiagnosticTrace.Mark(
                    _reader, $"mvd syntax escape_suffix bit={bit} value={bin}");
                if (bin is not (0 or 1))
                    throw new FormatException("CABAC bypass bins must be zero or one.");
                magnitude = checked(magnitude + (bin << bit));
            }
        }

        H264CabacDiagnosticTrace.Mark(_reader, $"mvd syntax sign begin magnitude={magnitude}");
        int sign = _reader.DecodeBypass();
        H264CabacDiagnosticTrace.Mark(_reader, $"mvd syntax sign={sign}");
        if (sign is not (0 or 1))
            throw new FormatException("CABAC bypass bins must be zero or one.");
        int result = sign == 0 ? magnitude : -magnitude;
        H264CabacDiagnosticTrace.Mark(_reader, $"mvd syntax complete value={result}");
        return result;
    }

    /// <summary>Decodes P-slice mb_type, including intra macroblock types.</summary>
    public int ReadPSliceMacroblockType()
    {
        if (ReadFlagAtContext(14))
            return 5 + ReadInterSliceIntraMacroblockType(17);

        if (!ReadFlagAtContext(15))
            return ReadFlagAtContext(16) ? 3 : 0;

        return ReadFlagAtContext(17) ? 1 : 2;
    }

    /// <summary>Decodes B-slice mb_type, including intra macroblock types.</summary>
    public int ReadBSliceMacroblockType(int firstBinContextIncrement)
    {
        if (firstBinContextIncrement is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(firstBinContextIncrement));
        if (!ReadFlagAtContext(27 + firstBinContextIncrement))
            return 0;
        if (!ReadFlagAtContext(30))
            return 1 + (ReadFlagAtContext(32) ? 1 : 0);

        if (!ReadFlagAtContext(31))
        {
            int partitionCode = (ReadFlagAtContext(32) ? 4 : 0) |
                (ReadFlagAtContext(32) ? 2 : 0) |
                (ReadFlagAtContext(32) ? 1 : 0);
            return 3 + partitionCode;
        }

        if (!ReadFlagAtContext(32))
        {
            int partitionCode = (ReadFlagAtContext(32) ? 4 : 0) |
                (ReadFlagAtContext(32) ? 2 : 0) |
                (ReadFlagAtContext(32) ? 1 : 0);
            return 12 + partitionCode;
        }

        int suffix = (ReadFlagAtContext(32) ? 2 : 0) |
            (ReadFlagAtContext(32) ? 1 : 0);
        return suffix switch
        {
            0 => 20 + (ReadFlagAtContext(32) ? 1 : 0),
            1 => 23 + ReadInterSliceIntraMacroblockType(32),
            2 => 11,
            3 => 22,
            _ => throw new InvalidOperationException()
        };
    }

    /// <summary>Decodes P-slice sub_mb_type (contexts 21..23).</summary>
    public int ReadPSubMacroblockType()
    {
        if (ReadFlagAtContext(21))
            return 0;
        if (!ReadFlagAtContext(22))
            return 1;
        return ReadFlagAtContext(23) ? 2 : 3;
    }

    /// <summary>Decodes B-slice sub_mb_type (contexts 36..39).</summary>
    public int ReadBSubMacroblockType()
    {
        if (!ReadFlagAtContext(36))
            return 0;
        if (!ReadFlagAtContext(37))
            return 1 + (ReadFlagAtContext(39) ? 1 : 0);

        int value = 3;
        if (ReadFlagAtContext(38))
        {
            if (ReadFlagAtContext(39))
                return 11 + (ReadFlagAtContext(39) ? 1 : 0);
            value += 4;
        }

        value += 2 * (ReadFlagAtContext(39) ? 1 : 0);
        value += ReadFlagAtContext(39) ? 1 : 0;
        return value;
    }

    private bool ReadFlagAtContext(int contextIndex)
    {
        int bin = _reader.DecodeBin(contextIndex);
        return bin switch
        {
            0 => false,
            1 => true,
            _ => throw new FormatException("CABAC decision bins must be zero or one.")
        };
    }

    /// <summary>
    /// Decodes mb_qp_delta (H.264 9.3.3.1.7). The caller supplies
    /// ctxIdxInc = condTermFlagA + condTermFlagB from neighbouring macroblocks.
    /// </summary>
    public int ReadMacroblockQpDelta(int contextIndexIncrement)
    {
        if (contextIndexIncrement is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));

        int codeNum = 0;
        while (true)
        {
            int contextIndex = codeNum == 0
                ? 60 + contextIndexIncrement
                : codeNum == 1 ? 62 : 63;
            int bin = _reader.DecodeBin(contextIndex);
            if (bin == 0)
                break;
            if (bin != 1)
                throw new FormatException("CABAC decision bins must be zero or one.");

            codeNum++;
            // mb_qp_delta is restricted to -26..25, which maps to codeNum 0..52.
            if (codeNum > 52)
                throw new FormatException("CABAC mb_qp_delta exceeds the supported 8-bit QP range.");
        }

        int delta = (codeNum & 1) == 0
            ? -(codeNum / 2)
            : (codeNum + 1) / 2;
        if (delta is < -26 or > 25)
            throw new FormatException("CABAC mb_qp_delta is outside the supported -26..25 range.");
        return delta;
    }
}
