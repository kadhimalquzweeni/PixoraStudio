namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Picture-relative neighbour state used to derive CABAC I-slice macroblock
/// header context increments. Only frame-coded, single-slice-group pictures
/// are represented by this tracker.
/// </summary>
public sealed class H264CabacIntraMacroblockNeighbourContext
{
    private readonly Dictionary<(int X, int Y), MacroblockState> _macroblocks = [];
    private readonly Dictionary<uint, MacroblockState> _macroblocksByAddress = [];

    public void Reset()
    {
        _macroblocks.Clear();
        _macroblocksByAddress.Clear();
    }

    public void RecordMacroblock(
        uint address,
        int macroblockX,
        int macroblockY,
        int sliceId,
        bool isIntraNxN,
        bool isIntra16x16,
        bool isPcm,
        uint codedBlockPatternLuma,
        uint codedBlockPatternChroma,
        uint? intraChromaPredictionMode,
        int? macroblockQpDelta)
    {
        if (macroblockX < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (macroblockY < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (codedBlockPatternLuma > 15)
            throw new ArgumentOutOfRangeException(nameof(codedBlockPatternLuma));
        if (codedBlockPatternChroma > 2)
            throw new ArgumentOutOfRangeException(nameof(codedBlockPatternChroma));
        if (intraChromaPredictionMode is > 3)
            throw new ArgumentOutOfRangeException(nameof(intraChromaPredictionMode));
        if (macroblockQpDelta is < -26 or > 25)
            throw new ArgumentOutOfRangeException(nameof(macroblockQpDelta));
        if (isPcm && (macroblockQpDelta.HasValue || intraChromaPredictionMode.HasValue))
            throw new ArgumentException("I_PCM macroblocks do not carry intra prediction or QP-delta syntax.");

        MacroblockState state = new(
            address, sliceId, isIntraNxN, isIntra16x16, isPcm,
            codedBlockPatternLuma, codedBlockPatternChroma,
            intraChromaPredictionMode, macroblockQpDelta);
        _macroblocks[(macroblockX, macroblockY)] = state;
        _macroblocksByAddress[address] = state;
    }

    /// <summary>
    /// For I-slice mb_type (ctxIdxOffset 3), a neighbour contributes one
    /// unless it is I_NxN. Unavailable neighbours contribute zero.
    /// </summary>
    public int GetMacroblockTypeContextIncrement(
        int macroblockX, int macroblockY, int sliceId)
    {
        ValidatePosition(macroblockX, macroblockY, sliceId);
        int left = GetSpatialNeighbour(macroblockX - 1, macroblockY, sliceId,
            state => state.IsIntraNxN ? 0 : 1);
        int top = GetSpatialNeighbour(macroblockX, macroblockY - 1, sliceId,
            state => state.IsIntraNxN ? 0 : 1);
        return left + top;
    }

    /// <summary>
    /// Derives intra_chroma_pred_mode ctxIdxInc: available non-PCM intra
    /// neighbours with a non-zero chroma prediction mode contribute one.
    /// </summary>
    public int GetChromaPredictionModeContextIncrement(
        int macroblockX, int macroblockY, int sliceId)
    {
        ValidatePosition(macroblockX, macroblockY, sliceId);
        int left = GetSpatialNeighbour(macroblockX - 1, macroblockY, sliceId,
            state => !state.IsPcm && state.IntraChromaPredictionMode is > 0 ? 1 : 0);
        int top = GetSpatialNeighbour(macroblockX, macroblockY - 1, sliceId,
            state => !state.IsPcm && state.IntraChromaPredictionMode is > 0 ? 1 : 0);
        return left + top;
    }

    /// <summary>
    /// Derives mb_qp_delta ctxIdxInc from the preceding macroblock in
    /// decoding order. The first macroblock in a slice has no predecessor.
    /// </summary>
    public int GetQpDeltaContextIncrement(uint? previousMacroblockAddress, int sliceId)
    {
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (previousMacroblockAddress is not uint address ||
            !_macroblocksByAddress.TryGetValue(address, out MacroblockState previous) ||
            previous.SliceId != sliceId || previous.IsPcm)
            return 0;

        if (!previous.IsIntra16x16 &&
            previous.CodedBlockPatternLuma == 0 &&
            previous.CodedBlockPatternChroma == 0)
            return 0;

        return previous.MacroblockQpDelta is int delta && delta != 0 ? 1 : 0;
    }

    /// <summary>
    /// Returns four luma coded_block_pattern context increments in raster
    /// order for the four 8x8 luma regions of the current macroblock.
    /// </summary>
    public int[] GetLumaCodedBlockPatternContextIncrements(
        int macroblockX, int macroblockY, int sliceId)
    {
        ValidatePosition(macroblockX, macroblockY, sliceId);
        int[] increments = new int[4];
        for (int blockIndex = 0; blockIndex < 4; blockIndex++)
        {
            int blockX = macroblockX * 2 + (blockIndex & 1);
            int blockY = macroblockY * 2 + (blockIndex >> 1);
            int left = GetLumaCbpNeighbourCondition(blockX - 1, blockY, sliceId);
            int top = GetLumaCbpNeighbourCondition(blockX, blockY - 1, sliceId);
            increments[blockIndex] = left + (2 * top);
        }
        return increments;
    }

    /// <summary>
    /// Returns context increments for the first and second chroma CBP bins.
    /// The second increment excludes the normative +4 bin-index offset,
    /// which the syntax reader adds through its ctxIdxOffset of 81.
    /// </summary>
    public int[] GetChromaCodedBlockPatternContextIncrements(
        int macroblockX, int macroblockY, int sliceId)
    {
        ValidatePosition(macroblockX, macroblockY, sliceId);
        int leftFirst = GetChromaCbpNeighbourCondition(
            macroblockX - 1, macroblockY, sliceId, secondBin: false);
        int topFirst = GetChromaCbpNeighbourCondition(
            macroblockX, macroblockY - 1, sliceId, secondBin: false);
        int leftSecond = GetChromaCbpNeighbourCondition(
            macroblockX - 1, macroblockY, sliceId, secondBin: true);
        int topSecond = GetChromaCbpNeighbourCondition(
            macroblockX, macroblockY - 1, sliceId, secondBin: true);

        return
        [
            leftFirst + (2 * topFirst),
            leftSecond + (2 * topSecond)
        ];
    }

    private int GetLumaCbpNeighbourCondition(int blockX, int blockY, int sliceId)
    {
        if (blockX < 0 || blockY < 0)
            return 0;
        int macroblockX = blockX / 2;
        int macroblockY = blockY / 2;
        if (!TryGetMacroblock(macroblockX, macroblockY, sliceId, out MacroblockState state) ||
            state.IsPcm)
            return 0;

        int neighbourBlockIndex = ((blockY & 1) * 2) + (blockX & 1);
        return (state.CodedBlockPatternLuma & (1u << neighbourBlockIndex)) == 0 ? 1 : 0;
    }

    private int GetChromaCbpNeighbourCondition(
        int macroblockX, int macroblockY, int sliceId, bool secondBin)
    {
        if (!TryGetMacroblock(macroblockX, macroblockY, sliceId, out MacroblockState state))
            return 0;
        if (state.IsPcm)
            return 1;

        return secondBin
            ? state.CodedBlockPatternChroma == 2 ? 1 : 0
            : state.CodedBlockPatternChroma != 0 ? 1 : 0;
    }

    private int GetSpatialNeighbour(
        int x, int y, int sliceId, Func<MacroblockState, int> selector)
    {
        return TryGetMacroblock(x, y, sliceId, out MacroblockState state)
            ? selector(state)
            : 0;
    }

    private bool TryGetMacroblock(
        int x, int y, int sliceId, out MacroblockState state)
    {
        if (x >= 0 && y >= 0 &&
            _macroblocks.TryGetValue((x, y), out state) &&
            state.SliceId == sliceId)
            return true;

        state = default;
        return false;
    }

    private static void ValidatePosition(int x, int y, int sliceId)
    {
        if (x < 0)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
    }

    private readonly record struct MacroblockState(
        uint Address,
        int SliceId,
        bool IsIntraNxN,
        bool IsIntra16x16,
        bool IsPcm,
        uint CodedBlockPatternLuma,
        uint CodedBlockPatternChroma,
        uint? IntraChromaPredictionMode,
        int? MacroblockQpDelta);
}
