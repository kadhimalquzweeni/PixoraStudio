namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Stores resolved luma Intra4x4 prediction modes and derives the predicted
/// mode for a block from its available left and top neighbours.
/// </summary>
public sealed class H264Intra4x4PredictionContext
{
    private readonly Dictionary<(int X, int Y), BlockState> _blocks = [];

    /// <summary>Clears all picture prediction-mode state.</summary>
    public void Reset() => _blocks.Clear();

    /// <summary>
    /// Records a resolved Intra4x4 mode at picture-relative 4x4 coordinates.
    /// </summary>
    public void RecordMode(int x, int y, int sliceId, uint predictionMode)
    {
        ValidateCoordinates(x, y);
        if (sliceId < 0) throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (predictionMode > 8)
            throw new ArgumentOutOfRangeException(nameof(predictionMode),
                "Intra4x4 prediction modes must be in the range 0..8.");

        _blocks[(x, y)] = new BlockState(sliceId, predictionMode);
    }

    /// <summary>
    /// Returns min(modeA, modeB) when both neighbours are available.
    /// If either neighbour is unavailable, the predicted mode is DC (2).
    /// </summary>
    public uint GetPredictedMode(int x, int y, int sliceId)
    {
        ValidateCoordinates(x, y);
        if (sliceId < 0) throw new ArgumentOutOfRangeException(nameof(sliceId));

        uint left = 0, top = 0;
        bool hasLeft = x > 0 && TryGetMode((x - 1, y), sliceId, out left);
        bool hasTop = y > 0 && TryGetMode((x, y - 1), sliceId, out top);

        if (!hasLeft || !hasTop)
            return 2;

        return Math.Min(left, top);
    }

    /// <summary>
    /// Resolves one parsed prev-mode/remainder entry using its current
    /// neighbour-derived prediction mode.
    /// </summary>
    public uint ResolveAndRecord(
        int x,
        int y,
        int sliceId,
        bool previousModeFlag,
        uint? remainderMode)
    {
        uint predicted = GetPredictedMode(x, y, sliceId);
        uint resolved = H264IntraPredictionModeReader.ResolveMode(
            previousModeFlag, remainderMode, predicted);
        RecordMode(x, y, sliceId, resolved);
        return resolved;
    }

    private bool TryGetMode((int X, int Y) coordinate, int sliceId, out uint mode)
    {
        if (_blocks.TryGetValue(coordinate, out BlockState state) &&
            state.SliceId == sliceId)
        {
            mode = state.PredictionMode;
            return true;
        }

        mode = 0;
        return false;
    }

    private static void ValidateCoordinates(int x, int y)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0) throw new ArgumentOutOfRangeException(nameof(y));
    }

    private readonly record struct BlockState(int SliceId, uint PredictionMode);
}
