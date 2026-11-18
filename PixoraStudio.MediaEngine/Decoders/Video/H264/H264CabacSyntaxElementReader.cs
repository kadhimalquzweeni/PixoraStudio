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
    public int ReadIntraSliceMacroblockType(int firstBinContextIncrement)
    {
        if (firstBinContextIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(firstBinContextIncrement));

        if (!ReadFlagAtContext(3 + firstBinContextIncrement))
            return 0;

        int pcmFlag = _reader.DecodeTerminate();
        if (pcmFlag is not (0 or 1))
            throw new FormatException("CABAC terminating bins must be zero or one.");
        if (pcmFlag == 1)
            return 25;

        int suffix = 0;
        int thirdSuffixBin = 0;
        for (int binIndex = 2; binIndex <= 6; binIndex++)
        {
            int contextIndex;
            if (binIndex == 2)
                contextIndex = 6;
            else if (binIndex == 3)
                contextIndex = 7;
            else if (binIndex == 4)
                contextIndex = thirdSuffixBin == 0 ? 9 : 8;
            else if (binIndex == 5)
                contextIndex = thirdSuffixBin == 0 ? 10 : 9;
            else
                contextIndex = 10;

            int bin = _reader.DecodeBin(contextIndex);
            if (bin is not (0 or 1))
                throw new FormatException("CABAC decision bins must be zero or one.");

            suffix = (suffix << 1) | bin;
            if (binIndex == 3)
                thirdSuffixBin = bin;
        }

        if (suffix > 23)
            throw new FormatException("The I-slice mb_type suffix is outside the defined I16x16 range.");
        return suffix + 1;
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
        return bin switch
        {
            0 => false,
            1 => true,
            _ => throw new FormatException("CABAC terminating bins must be zero or one.")
        };
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
        if (lumaArrayTypeInvalid(chromaArrayType))
            throw new ArgumentOutOfRangeException(nameof(chromaArrayType));
        if (lumaContextIncrements.Count != 4 ||
            lumaContextIncrements.Any(value => value is < 0 or > 3))
            throw new ArgumentException(
                "Four luma coded-block context increments in the range 0..3 are required.",
                nameof(lumaContextIncrements));

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
            ArgumentNullException.ThrowIfNull(chromaContextIncrements);
            if (chromaContextIncrements.Count != 2 ||
                chromaContextIncrements.Any(value => value is < 0 or > 3))
                throw new ArgumentException(
                    "Two chroma coded-block context increments in the range 0..3 are required.",
                    nameof(chromaContextIncrements));

            int firstBin = _reader.DecodeBin(77 + chromaContextIncrements[0]);
            if (firstBin is not (0 or 1))
                throw new FormatException("CABAC decision bins must be zero or one.");
            if (firstBin == 1)
            {
                int secondBin = _reader.DecodeBin(81 + chromaContextIncrements[1]);
                if (secondBin is not (0 or 1))
                    throw new FormatException("CABAC decision bins must be zero or one.");
                chromaPattern = secondBin == 0 ? 1u : 2u;
            }
        }

        uint combinedPattern = lumaPattern | (chromaPattern << 4);
        return new H264CodedBlockPattern(
            combinedPattern, lumaPattern, chromaPattern, chromaArrayType);
    }

    private static bool lumaArrayTypeInvalid(uint chromaArrayType) =>
        chromaArrayType > 3;

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
