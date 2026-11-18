namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Tracks CABAC coded-block flags on a picture-relative 4x4 block grid.
/// Separate plane identifiers keep luma, Cb and Cr neighbour state isolated.
/// </summary>
public sealed class H264CabacResidualNeighbourContext
{
    private readonly Dictionary<(int Plane, int X, int Y), BlockState> _blocks = [];
    private readonly Dictionary<(int Plane, int X, int Y), MacroblockState> _macroblocks = [];

    public void Reset()
    {
        _blocks.Clear();
        _macroblocks.Clear();
    }

    /// <summary>
    /// Marks a macroblock as available before its residual blocks are parsed.
    /// PCM macroblocks imply coded_block_flag=1 for all of their blocks.
    /// </summary>
    public void RecordMacroblock(
        int plane, int macroblockX, int macroblockY, int sliceId, bool isPcm)
    {
        ValidatePlane(plane);
        ValidateCoordinate(macroblockX, nameof(macroblockX));
        ValidateCoordinate(macroblockY, nameof(macroblockY));
        ValidateSliceId(sliceId);
        _macroblocks[(plane, macroblockX, macroblockY)] =
            new MacroblockState(sliceId, isPcm);
    }

    /// <summary>Records one decoded transform block's coded_block_flag.</summary>
    public void RecordBlock(
        int plane, int blockX, int blockY, int sliceId, bool codedBlockFlag)
    {
        ValidatePlane(plane);
        ValidateCoordinate(blockX, nameof(blockX));
        ValidateCoordinate(blockY, nameof(blockY));
        ValidateSliceId(sliceId);
        _blocks[(plane, blockX, blockY)] = new BlockState(sliceId, codedBlockFlag);
    }

    /// <summary>
    /// Derives the context increment for the current block from its left and
    /// top neighbours. Coordinates and macroblock block dimensions are in
    /// 4x4 transform-block units.
    /// </summary>
    public int GetContextIndexIncrement(
        int plane,
        int blockX,
        int blockY,
        int blocksPerMacroblockX,
        int blocksPerMacroblockY,
        int sliceId,
        bool currentMacroblockIsIntra)
    {
        ValidatePlane(plane);
        ValidateCoordinate(blockX, nameof(blockX));
        ValidateCoordinate(blockY, nameof(blockY));
        ValidateSliceId(sliceId);
        if (blocksPerMacroblockX <= 0)
            throw new ArgumentOutOfRangeException(nameof(blocksPerMacroblockX));
        if (blocksPerMacroblockY <= 0)
            throw new ArgumentOutOfRangeException(nameof(blocksPerMacroblockY));

        H264CabacCodedBlockNeighbour left = GetNeighbour(
            plane, blockX - 1, blockY, blocksPerMacroblockX,
            blocksPerMacroblockY, sliceId);
        H264CabacCodedBlockNeighbour top = GetNeighbour(
            plane, blockX, blockY - 1, blocksPerMacroblockX,
            blocksPerMacroblockY, sliceId);

        return H264CabacCodedBlockFlagContext.GetContextIndexIncrement(
            currentMacroblockIsIntra, left, top);
    }

    private H264CabacCodedBlockNeighbour GetNeighbour(
        int plane,
        int blockX,
        int blockY,
        int blocksPerMacroblockX,
        int blocksPerMacroblockY,
        int sliceId)
    {
        if (blockX < 0 || blockY < 0)
            return new H264CabacCodedBlockNeighbour(false, false, false, false);

        int macroblockX = blockX / blocksPerMacroblockX;
        int macroblockY = blockY / blocksPerMacroblockY;
        if (!_macroblocks.TryGetValue(
                (plane, macroblockX, macroblockY), out MacroblockState macroblock) ||
            macroblock.SliceId != sliceId)
            return new H264CabacCodedBlockNeighbour(false, false, false, false);

        if (macroblock.IsPcm)
            return new H264CabacCodedBlockNeighbour(true, false, true, true);

        if (_blocks.TryGetValue((plane, blockX, blockY), out BlockState block) &&
            block.SliceId == sliceId)
            return new H264CabacCodedBlockNeighbour(true, true, false, block.CodedBlockFlag);

        return new H264CabacCodedBlockNeighbour(true, false, false, false);
    }

    private static void ValidatePlane(int plane)
    {
        if (plane is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(plane));
    }

    private static void ValidateCoordinate(int value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateSliceId(int sliceId)
    {
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
    }

    private readonly record struct MacroblockState(int SliceId, bool IsPcm);
    private readonly record struct BlockState(int SliceId, bool CodedBlockFlag);
}
