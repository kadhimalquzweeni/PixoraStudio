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
    /// Decodes mb_skip_flag for P or B slices. The context increment is the
    /// sum of the left and upper neighbour condTermFlag values.
    /// </summary>
    public bool ReadMacroblockSkipFlag(bool isBSlice, int contextIndexIncrement)
    {
        if (contextIndexIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(contextIndexIncrement));
        return ReadFlagAtContext((isBSlice ? 24 : 11) + contextIndexIncrement);
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
