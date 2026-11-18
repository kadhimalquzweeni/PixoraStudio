namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Tracks the spatial neighbour flags required by CABAC P/B macroblock
/// headers. State is scoped to a slice for availability derivation.
/// </summary>
public sealed class H264CabacInterMacroblockNeighbourContext
{
    private readonly Dictionary<(int X, int Y), MacroblockState> _macroblocks = [];

    public void Reset() => _macroblocks.Clear();

    public void RecordMacroblock(
        uint address,
        int macroblockX,
        int macroblockY,
        int sliceId,
        bool isSkipped,
        bool isDirect)
    {
        if (macroblockX < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (macroblockY < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));

        _macroblocks[(macroblockX, macroblockY)] =
            new MacroblockState(address, sliceId, isSkipped, isDirect);
    }

    /// <summary>
    /// mb_skip_flag ctxIdxInc: one for each available left/top neighbour
    /// that is not skipped.
    /// </summary>
    public int GetSkipFlagContextIncrement(
        int macroblockX, int macroblockY, int sliceId)
    {
        ValidatePosition(macroblockX, macroblockY, sliceId);
        int left = GetNeighbour(macroblockX - 1, macroblockY, sliceId,
            state => state.IsSkipped ? 0 : 1);
        int top = GetNeighbour(macroblockX, macroblockY - 1, sliceId,
            state => state.IsSkipped ? 0 : 1);
        return left + (2 * top);
    }

    /// <summary>
    /// B-slice mb_type ctxIdxInc: one for each available left/top neighbour
    /// that is not direct.
    /// </summary>
    public int GetBSliceMacroblockTypeContextIncrement(
        int macroblockX, int macroblockY, int sliceId)
    {
        ValidatePosition(macroblockX, macroblockY, sliceId);
        int left = GetNeighbour(macroblockX - 1, macroblockY, sliceId,
            state => state.IsDirect ? 0 : 1);
        int top = GetNeighbour(macroblockX, macroblockY - 1, sliceId,
            state => state.IsDirect ? 0 : 1);
        return left + top;
    }

    private int GetNeighbour(
        int x, int y, int sliceId, Func<MacroblockState, int> selector)
    {
        return x >= 0 && y >= 0 &&
            _macroblocks.TryGetValue((x, y), out MacroblockState state) &&
            state.SliceId == sliceId
                ? selector(state)
                : 0;
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
        uint Address, int SliceId, bool IsSkipped, bool IsDirect);
}
