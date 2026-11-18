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
        if (!pps.EntropyCodingModeFlag)
            throw new ArgumentException("The supplied PPS must enable CABAC.", nameof(pps));
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (macroblockX < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (macroblockY < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        uint chromaArrayType = sps.SeparateColourPlaneFlag ? 0u : sps.ChromaFormatIdc;
        if (chromaArrayType is not (1 or 2))
            throw new NotSupportedException("CABAC inter residual syntax currently supports 4:2:0 and 4:2:2 chroma.");

        Func<int, uint, int> lumaContextProvider = (binIndex, decodedPattern) =>
            _neighbours.GetLumaCodedBlockPatternContextIncrement(
                macroblockX, macroblockY, sliceId, binIndex, decodedPattern);
        int[] chromaContextIncrements =
            _neighbours.GetChromaCodedBlockPatternContextIncrements(
                macroblockX, macroblockY, sliceId);

        H264CodedBlockPattern codedBlockPattern = _syntaxReader.ReadIntraCodedBlockPattern(
            lumaContextProvider, chromaContextIncrements, chromaArrayType);

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
            uint? previousAddress = macroblockAddress == 0
                ? null
                : macroblockAddress - 1;
            int qpContextIncrement = _neighbours.GetQpDeltaContextIncrement(
                previousAddress, sliceId);
            qpDelta = _syntaxReader.ReadMacroblockQpDelta(qpContextIncrement);
        }

        return new H264InterMacroblockResidualSyntax(
            codedBlockPattern, qpDelta)
        {
            TransformSize8X8Flag = transformSize8x8Flag
        };
    }
}
