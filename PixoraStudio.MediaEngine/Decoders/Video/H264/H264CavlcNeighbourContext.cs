using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Maintains decoded 4x4 block TotalCoeff values and derives the CAVLC nC
/// context from the available left and top neighbours.
/// </summary>
public sealed class H264CavlcNeighbourContext
{
    private readonly Dictionary<(int X, int Y), BlockState> _blocks = [];
    private readonly H264CavlcResidualBlockReader _residualReader;

    public H264CavlcNeighbourContext()
        : this(new H264CavlcResidualBlockReader())
    {
    }

    public H264CavlcNeighbourContext(H264CavlcResidualBlockReader residualReader)
    {
        ArgumentNullException.ThrowIfNull(residualReader);
        _residualReader = residualReader;
    }

    /// <summary>Removes all decoded block state, ready for a new picture.</summary>
    public void Reset() => _blocks.Clear();

    /// <summary>
    /// Records a decoded block's TotalCoeff at its picture-relative 4x4
    /// coordinate and associates it with the slice that decoded it.
    /// </summary>
    public void RecordBlock(int x, int y, int sliceId, int totalCoefficients)
    {
        ValidateCoordinate(x, y);
        if (sliceId < 0) throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (totalCoefficients is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(totalCoefficients));

        _blocks[(x, y)] = new BlockState(sliceId, totalCoefficients);
    }

    /// <summary>
    /// Derives nC from left (A) and top (B) neighbours. A neighbour is
    /// unavailable if it is outside the picture, not yet decoded, or belongs
    /// to another slice. When both are available nC=(nA+nB+1)&gt;&gt;1.
    /// </summary>
    public int GetPredictedNonZeroCount(int x, int y, int sliceId)
    {
        ValidateCoordinate(x, y);
        if (sliceId < 0) throw new ArgumentOutOfRangeException(nameof(sliceId));

        bool hasLeft = x > 0 && TryGetNeighbour((x - 1, y), sliceId, out int left);
        bool hasTop = y > 0 && TryGetNeighbour((x, y - 1), sliceId, out int top);

        if (hasLeft && hasTop) return (left + top + 1) >> 1;
        if (hasLeft) return left;
        if (hasTop) return top;
        return 0;
    }

    /// <summary>
    /// Decodes a residual block using the nC derived from its neighbours,
    /// then records TotalCoeff for subsequent blocks in decoding order.
    /// </summary>
    public H264CavlcResidualBlock ReadBlock(
        H264BitReader reader,
        int x,
        int y,
        int sliceId,
        int maximumCoefficients = 16)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int predictedCount = GetPredictedNonZeroCount(x, y, sliceId);
        H264CavlcResidualBlock block = _residualReader.ReadBlock(
            reader, predictedCount, maximumCoefficients);
        RecordBlock(x, y, sliceId, block.TotalCoefficients);
        return block;
    }

    private bool TryGetNeighbour((int X, int Y) coordinate, int sliceId, out int totalCoefficients)
    {
        if (_blocks.TryGetValue(coordinate, out BlockState state) && state.SliceId == sliceId)
        {
            totalCoefficients = state.TotalCoefficients;
            return true;
        }

        totalCoefficients = 0;
        return false;
    }

    private static void ValidateCoordinate(int x, int y)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0) throw new ArgumentOutOfRangeException(nameof(y));
    }

    private readonly record struct BlockState(int SliceId, int TotalCoefficients);
}
