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
