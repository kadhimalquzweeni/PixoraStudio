namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Tracks reference-index and motion-vector-difference values on the 4x4
/// motion-partition grid for CABAC P/B context derivation.
/// </summary>
public sealed class H264CabacMotionVectorNeighbourContext
{
    private readonly Dictionary<(int List, int X, int Y), MotionState> _states = [];

    public void Reset() => _states.Clear();

    /// <summary>
    /// Records the list reference and MVD for one 4x4 cell. A partition caller
    /// records the same values for every cell covered by that partition.
    /// </summary>
    public void Record(
        int list,
        int blockX,
        int blockY,
        int sliceId,
        int referenceIndex,
        bool isDirect,
        int mvdX,
        int mvdY)
    {
        ValidateList(list);
        ValidatePosition(blockX, blockY, sliceId);
        if (referenceIndex is < -1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(referenceIndex));

        _states[(list, blockX, blockY)] =
            new MotionState(sliceId, referenceIndex, isDirect, mvdX, mvdY);
    }

    /// <summary>
    /// ref_idx_lX ctxIdxInc: left contributes one and top contributes two
    /// when its reference index is non-zero; B direct neighbours contribute
    /// zero even if a cached reference value is present.
    /// </summary>
    public int GetReferenceIndexContextIncrement(
        int list,
        int blockX,
        int blockY,
        int sliceId,
        bool isBSlice)
    {
        ValidateList(list);
        ValidatePosition(blockX, blockY, sliceId);
        int left = GetNeighbour(list, blockX - 1, blockY, sliceId,
            state => state.ReferenceIndex > 0 && (!isBSlice || !state.IsDirect) ? 1 : 0);
        int top = GetNeighbour(list, blockX, blockY - 1, sliceId,
            state => state.ReferenceIndex > 0 && (!isBSlice || !state.IsDirect) ? 1 : 0);
        return left + (2 * top);
    }

    /// <summary>
    /// mvd_lX ctxIdxInc: 0 when the sum of absolute left/top MVD components
    /// is below 3, 1 for 3..32, and 2 for 33 or greater.
    /// </summary>
    public int GetMotionVectorDifferenceContextIncrement(
        int list,
        int blockX,
        int blockY,
        int component,
        int sliceId)
    {
        ValidateList(list);
        ValidatePosition(blockX, blockY, sliceId);
        if (component is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(component));

        int left = GetNeighbour(list, blockX - 1, blockY, sliceId,
            state => state.ReferenceIndex <= 0 || state.IsDirect
                ? 0
                : Math.Abs(component == 0 ? state.MvdX : state.MvdY));
        int top = GetNeighbour(list, blockX, blockY - 1, sliceId,
            state => state.ReferenceIndex <= 0 || state.IsDirect
                ? 0
                : Math.Abs(component == 0 ? state.MvdX : state.MvdY));
        int sum = checked(left + top);
        return sum < 3 ? 0 : sum < 33 ? 1 : 2;
    }

    private int GetNeighbour(
        int list,
        int x,
        int y,
        int sliceId,
        Func<MotionState, int> selector)
    {
        return x >= 0 && y >= 0 &&
            _states.TryGetValue((list, x, y), out MotionState state) &&
            state.SliceId == sliceId
                ? selector(state)
                : 0;
    }

    private static void ValidateList(int list)
    {
        if (list is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(list));
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

    private readonly record struct MotionState(
        int SliceId, int ReferenceIndex, bool IsDirect, int MvdX, int MvdY);
}
