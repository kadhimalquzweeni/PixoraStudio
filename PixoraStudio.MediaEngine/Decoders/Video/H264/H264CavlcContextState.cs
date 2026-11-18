namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Tracks decoded 4x4-block TotalCoeff values for CAVLC nC derivation.
/// Coordinates are in the picture's 4x4 luma-block grid. A block is usable
/// as a neighbour only after it has been decoded in the same slice.
/// </summary>
public sealed class H264CavlcContextState
{
    private readonly int[,] _totalCoefficients;
    private readonly int[,] _sliceIds;
    private readonly bool[,] _decoded;

    public H264CavlcContextState(int widthInBlocks, int heightInBlocks)
    {
        if (widthInBlocks <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthInBlocks));
        if (heightInBlocks <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightInBlocks));

        WidthInBlocks = widthInBlocks;
        HeightInBlocks = heightInBlocks;
        _totalCoefficients = new int[heightInBlocks, widthInBlocks];
        _sliceIds = new int[heightInBlocks, widthInBlocks];
        _decoded = new bool[heightInBlocks, widthInBlocks];
    }

    public int WidthInBlocks { get; }
    public int HeightInBlocks { get; }

    /// <summary>
    /// Returns nC for a block using the H.264 CAVLC neighbouring-block rule.
    /// Unavailable neighbours (picture edge, not yet decoded, or another
    /// slice) are treated as unavailable rather than as zero-coefficient blocks.
    /// </summary>
    public int GetPredictedNonZeroCount(int blockX, int blockY, int sliceId)
    {
        ValidateCoordinate(blockX, blockY);

        bool hasA = TryGetDecodedNeighbour(blockX - 1, blockY, sliceId, out int nA);
        bool hasB = TryGetDecodedNeighbour(blockX, blockY - 1, sliceId, out int nB);

        if (hasA && hasB)
            return (nA + nB + 1) >> 1;
        if (hasA)
            return nA;
        if (hasB)
            return nB;
        return 0;
    }

    /// <summary>Records a decoded block's TotalCoeff for subsequent neighbouring blocks.</summary>
    public void SetDecodedBlock(int blockX, int blockY, int sliceId, int totalCoefficients)
    {
        ValidateCoordinate(blockX, blockY);
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (totalCoefficients is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(totalCoefficients));

        _totalCoefficients[blockY, blockX] = totalCoefficients;
        _sliceIds[blockY, blockX] = sliceId;
        _decoded[blockY, blockX] = true;
    }

    /// <summary>
    /// Decodes one ordinary 4x4 luma/chroma AC block using its derived nC,
    /// then records TotalCoeff. Chroma DC blocks use their dedicated VLC
    /// tables and must continue to be decoded through ReadBlock directly.
    /// </summary>
    public Models.Video.H264CavlcResidualBlock ReadBlock(
        H264CavlcResidualBlockReader reader,
        H264BitReader bitReader,
        int blockX,
        int blockY,
        int sliceId,
        int maximumCoefficients = 16)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(bitReader);
        if (maximumCoefficients is not (15 or 16))
            throw new ArgumentOutOfRangeException(nameof(maximumCoefficients),
                "Neighbour-derived nC is used for 15- or 16-coefficient blocks.");

        int predictedCount = GetPredictedNonZeroCount(blockX, blockY, sliceId);
        Models.Video.H264CavlcResidualBlock block =
            reader.ReadBlock(bitReader, predictedCount, maximumCoefficients);
        SetDecodedBlock(blockX, blockY, sliceId, block.TotalCoefficients);
        return block;
    }

    private bool TryGetDecodedNeighbour(int x, int y, int sliceId, out int totalCoefficients)
    {
        totalCoefficients = 0;
        if (x < 0 || y < 0 || x >= WidthInBlocks || y >= HeightInBlocks)
            return false;
        if (!_decoded[y, x] || _sliceIds[y, x] != sliceId)
            return false;

        totalCoefficients = _totalCoefficients[y, x];
        return true;
    }

    private void ValidateCoordinate(int x, int y)
    {
        if ((uint)x >= (uint)WidthInBlocks)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)HeightInBlocks)
            throw new ArgumentOutOfRangeException(nameof(y));
    }
}