using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parses CABAC coded_block_pattern, transform_size_8x8_flag and mb_qp_delta
/// for inter P/B macroblocks, using the same spatial CBP/QP contexts as intra
/// macroblocks.
/// </summary>
public sealed class H264CabacInterMacroblockResidualSyntaxReader
{
    private readonly H264CabacSyntaxElementReader _syntaxReader;
    private readonly H264CabacIntraMacroblockNeighbourContext _neighbours;

    public H264CabacInterMacroblockResidualSyntaxReader(
        H264CabacSyntaxElementReader syntaxReader,
        H264CabacIntraMacroblockNeighbourContext neighbours)
    {
        _syntaxReader = syntaxReader ?? throw new ArgumentNullException(nameof(syntaxReader));
        _neighbours = neighbours ?? throw new ArgumentNullException(nameof(neighbours));
    }

    public H264InterMacroblockResidualSyntax Read(
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        int macroblockX,
        int macroblockY,
        int sliceId,
        uint macroblockAddress,
        bool noSubMacroblockPartitionSmallerThan8x8)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        if (macroblockX < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (macroblockY < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (sps.ChromaArrayType is not (1 or 2))
            throw new NotSupportedException("CABAC inter residual syntax currently supports 4:2:0 and 4:2:2 chroma.");

        Func<int, uint, int> lumaContextProvider = (binIndex, decodedPattern) =>
            _neighbours.GetLumaCodedBlockPatternContextIncrement(
                macroblockX, macroblockY, sliceId, binIndex, decodedPattern);
        int[] chromaContextIncrements =
            _neighbours.GetChromaCodedBlockPatternContextIncrements(
                macroblockX, macroblockY, sliceId);

        H264CodedBlockPattern codedBlockPattern = _syntaxReader.ReadIntraCodedBlockPattern(
            lumaContextIncrements: null,
            chromaContextIncrements,
            sps.ChromaArrayType,
            isIntra16x16: false,
            lumaContextProvider);

        bool transformSize8x8Flag = false;
        if (pps.Transform8X8ModeFlag &&
            codedBlockPattern.Luma != 0 &&
            noSubMacroblockPartitionSmallerThan8x8)
        {
            int increment = _neighbours.GetTransformSize8x8ContextIncrement(
                macroblockX, macroblockY, sliceId);
            transformSize8x8Flag = _syntaxReader.ReadTransformSize8x8Flag(increment);
        }

        int? qpDelta = null;
        if (codedBlockPattern.Luma != 0 || codedBlockPattern.Chroma != 0)
        {
            int previousAddress = macroblockAddress == 0
                ? -1
                : checked((int)macroblockAddress - 1);
            int qpContextIncrement = _neighbours.GetQpDeltaContextIncrement(
                previousAddress, sliceId);
            qpDelta = _syntaxReader.ReadMacroblockQpDelta(qpContextIncrement);
        }

        return new H264InterMacroblockResidualSyntax(
            codedBlockPattern, qpDelta, transformSize8x8Flag);
    }
}
