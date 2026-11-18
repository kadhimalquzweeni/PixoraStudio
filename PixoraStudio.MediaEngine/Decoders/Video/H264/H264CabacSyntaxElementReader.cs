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

        int lumaCbpFlag = ReadFlagAtContext(contextBase + 3) ? 1 : 0;
        int chromaCbpFlag = ReadFlagAtContext(contextBase + 4) ? 1 : 0;
        int chromaCbpValue = 0;
        if (chromaCbpFlag == 1)
        {
            int chromaContext = contextBase + (isIntraSlice ? 5 : 4);
            chromaCbpValue = ReadFlagAtContext(chromaContext) ? 1 : 0;
        }

        int predictionModeHigh = ReadFlagAtContext(
            contextBase + (isIntraSlice ? 6 : 5)) ? 1 : 0;
        int predictionModeLow = ReadFlagAtContext(
            contextBase + (isIntraSlice ? 7 : 5)) ? 1 : 0;

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
        return ReadFlagAtContext((isBSlice ? 24 : 11) + contextIndexIncrement);
    }

    /// <summary>
    /// Decodes ref_idx_l0 or ref_idx_l1 as truncated unary. The caller supplies
    /// the neighbour-derived context increment and the largest permitted index.
    /// </summary>
    public int ReadReferenceIndex(int contextIndexIncrement, int maximumReferenceIndex)
    {
        if (contextIndexIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));
        if (maximumReferenceIndex is < 0 or > 31)
            throw new ArgumentOutOfRangeException(nameof(maximumReferenceIndex));

        for (int binIndex = 0; binIndex < maximumReferenceIndex; binIndex++)
        {
            int contextIndex = 54 + contextIndexIncrement + Math.Min(binIndex, 2);
            int bin = _reader.DecodeBin(contextIndex);
            if (bin == 0)
                return binIndex;
            if (bin != 1)
                throw new FormatException("CABAC decision bins must be zero or one.");
        }

        return maximumReferenceIndex;
    }

    /// <summary>Decodes end_of_slice_flag using the CABAC terminating-bin process.</summary>
    public bool ReadEndOfSliceFlag()
    {
        int bin = _reader.DecodeTerminate();
        if (bin == 0)
            return false;
        if (bin == 1)
        {
            _reader.MarkTerminated();
            return true;
        }
        throw new FormatException("CABAC terminating bins must be zero or one.");
    }

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
            value = (value << 1) | bin;
        }
        return value;
    }

    /// <summary>Decodes transform_size_8x8_flag (ctxIdx 399).</summary>
    public bool ReadTransformSize8x8Flag() =>
        ReadFlagAtContext(399);

    /// <summary>
    /// Decodes intra_chroma_pred_mode as truncated unary (ctxIdx offset 64).
    /// The caller supplies the neighbouring-mode context increment.
    /// </summary>
    public int ReadIntraChromaPredictionMode(int contextIndexIncrement)
    {
        if (contextIndexIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));

        for (int binIndex = 0; binIndex < 3; binIndex++)
        {
            int contextIndex = 64 + contextIndexIncrement + Math.Min(binIndex, 1);
            int bin = _reader.DecodeBin(contextIndex);
            if (bin == 0)
                return binIndex;
            if (bin != 1)
                throw new FormatException("CABAC decision bins must be zero or one.");
        }

        return 3;
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
        if (contextIndexIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));

        int codeNum = 0;
        while (true)
        {
            int contextIndex = 60 + contextIndexIncrement + Math.Min(codeNum, 2);
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
