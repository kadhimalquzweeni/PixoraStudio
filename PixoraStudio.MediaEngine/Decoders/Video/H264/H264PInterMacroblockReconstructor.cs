using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Orchestrates one complete CAVLC P inter macroblock: list-0 motion
/// compensation, weighted prediction, coded-block-pattern/QP syntax,
/// CAVLC residual parsing, inverse transforms and destination surface commit.
/// This stage handles coded inter macroblocks; P_Skip has a separate method.
/// </summary>
public sealed class H264PInterMacroblockReconstructor
{
    private readonly H264InterMacroblockPredictionDecoder _predictionDecoder;
    private readonly H264InterMacroblockResidualSyntaxReader _residualSyntaxReader = new();
    private readonly H264InterResidualMacroblockReader _residualReader = new();

    public H264PInterMacroblockReconstructor()
        : this(new H264InterMacroblockPredictionDecoder())
    {
    }

    public H264PInterMacroblockReconstructor(
        H264InterMacroblockPredictionDecoder predictionDecoder)
    {
        ArgumentNullException.ThrowIfNull(predictionDecoder);
        _predictionDecoder = predictionDecoder;
    }

    public H264InterMacroblockReconstructionResult DecodeAndReconstruct(
        H264BitReader reader,
        H264MacroblockHeader header,
        uint numRefIdxL0ActiveMinus1,
        IReadOnlyList<H264DecodedPictureSurface> referenceList0,
        H264MotionVectorPictureState motionVectorState,
        H264DecodedPictureSurface destination,
        int macroblockX,
        int macroblockY,
        int sliceId,
        int previousQpY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        H264CavlcNeighbourContext lumaNeighbours,
        H264CavlcNeighbourContext chromaCbNeighbours,
        H264CavlcNeighbourContext chromaCrNeighbours,
        H264PredictionWeightTable? predictionWeights = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(referenceList0);
        ArgumentNullException.ThrowIfNull(motionVectorState);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        ArgumentNullException.ThrowIfNull(lumaNeighbours);
        ArgumentNullException.ThrowIfNull(chromaCbNeighbours);
        ArgumentNullException.ThrowIfNull(chromaCrNeighbours);

        ValidateContext(header, destination, macroblockX, macroblockY,
            sliceId, previousQpY, sps, pps);
        if (header.IsSkipped || header.IsIntra || header.IsPcm)
            throw new ArgumentException("The header must describe a coded inter P macroblock.", nameof(header));
        if (destination.IsMacroblockDecoded(macroblockX, macroblockY))
            throw new InvalidOperationException("The destination macroblock has already been decoded.");

        IReadOnlyList<H264InterMacroblockPrediction> partitions =
            _predictionDecoder.DecodeAndPredict(
                reader, header, numRefIdxL0ActiveMinus1, referenceList0,
                motionVectorState, macroblockX, macroblockY, sliceId,
                predictionWeights);

        bool noSubMbPartSizeLessThan8x8Flag = partitions.All(item =>
            item.Partition.Width >= 2 && item.Partition.Height >= 2);
        H264InterMacroblockResidualSyntax residualSyntax =
            _residualSyntaxReader.Read(
                reader, header, sps, pps, noSubMbPartSizeLessThan8x8Flag);
        int qpY = residualSyntax.MacroblockQpDelta is int delta
            ? UpdateQpY(previousQpY, delta)
            : previousQpY;

        H264InterResidualMacroblock residual = _residualReader.Read(
            reader, header, residualSyntax.CodedBlockPattern, sps,
            macroblockX, macroblockY, sliceId,
            lumaNeighbours, chromaCbNeighbours, chromaCrNeighbours);

        H264MotionCompensatedBlock prediction = H264InterPredictionAssembler.Assemble(
            partitions, sps.ChromaFormatIdc);
        H264MotionCompensatedBlock reconstructed =
            H264InterMacroblockResidualReconstructor.Reconstruct(
                prediction, residual, qpY, sps, pps);

        destination.WriteMacroblock(
            macroblockX, macroblockY, sliceId, false,
            reconstructed.Luma, reconstructed.Cb, reconstructed.Cr);

        return new H264InterMacroblockReconstructionResult(
            header.MacroblockAddress, qpY, residualSyntax.CodedBlockPattern,
            partitions, prediction, reconstructed);
    }

    /// <summary>
    /// Reconstructs and commits one P_Skip macroblock. No coded_block_pattern,
    /// mb_qp_delta or residual syntax is present for a skipped macroblock.
    /// </summary>
    public H264InterMacroblockReconstructionResult DecodeSkippedAndReconstruct(
        H264MacroblockHeader header,
        IReadOnlyList<H264DecodedPictureSurface> referenceList0,
        H264MotionVectorPictureState motionVectorState,
        H264DecodedPictureSurface destination,
        int macroblockX,
        int macroblockY,
        int sliceId,
        int currentQpY,
        H264SequenceParameterSet sps,
        H264PredictionWeightTable? predictionWeights = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(referenceList0);
        ArgumentNullException.ThrowIfNull(motionVectorState);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sps);

        if (!header.IsSkipped || header.SkippedMacroblockCount != 1)
            throw new ArgumentException("A single P_Skip macroblock header is required.", nameof(header));
        if (currentQpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(currentQpY));
        ValidateSurface(destination, macroblockX, macroblockY, sliceId, sps);
        if (header.MacroblockAddress !=
            checked((uint)(macroblockY * destination.MacroblockWidth + macroblockX)))
            throw new FormatException("The skipped macroblock address does not match its picture coordinates.");
        if (destination.IsMacroblockDecoded(macroblockX, macroblockY))
            throw new InvalidOperationException("The destination macroblock has already been decoded.");

        H264InterMacroblockPrediction partition =
            _predictionDecoder.DecodeSkippedAndPredict(
                header, referenceList0, motionVectorState,
                macroblockX, macroblockY, sliceId, predictionWeights);
        H264MotionCompensatedBlock prediction = H264InterPredictionAssembler.Assemble(
            [partition], sps.ChromaFormatIdc);

        destination.WriteMacroblock(
            macroblockX, macroblockY, sliceId, false,
            prediction.Luma, prediction.Cb, prediction.Cr);

        H264CodedBlockPattern emptyPattern = new(0, 0, 0, sps.ChromaFormatIdc);
        return new H264InterMacroblockReconstructionResult(
            header.MacroblockAddress, currentQpY, emptyPattern,
            [partition], prediction, prediction);
    }

    private static void ValidateContext(
        H264MacroblockHeader header,
        H264DecodedPictureSurface destination,
        int x,
        int y,
        int sliceId,
        int qpY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        if (qpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(qpY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (pps.EntropyCodingModeFlag)
            throw new NotSupportedException("CABAC P inter macroblock reconstruction is not implemented.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (header.MacroblockAddress != checked((uint)(y * destination.MacroblockWidth + x)))
            throw new FormatException("The macroblock header address does not match its picture coordinates.");
        ValidateSurface(destination, x, y, sliceId, sps);
    }

    private static void ValidateSurface(
        H264DecodedPictureSurface destination,
        int x,
        int y,
        int sliceId,
        H264SequenceParameterSet sps)
    {
        if ((uint)x >= (uint)destination.MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)destination.MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (!sps.FrameMbsOnlyFlag || sps.SeparateColourPlaneFlag ||
            sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0 ||
            sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("P inter macroblock reconstruction supports frame-only 8-bit non-separate 4:2:0 and 4:2:2 only.");
        if (sps.PicWidthInMbsMinus1 + 1 != (uint)destination.MacroblockWidth ||
            sps.PicHeightInMapUnitsMinus1 + 1 != (uint)destination.MacroblockHeight ||
            sps.ChromaFormatIdc != destination.ChromaFormatIdc)
            throw new ArgumentException("The SPS does not describe the destination picture surface.", nameof(sps));
    }

    private static int UpdateQpY(int previousQpY, int delta)
    {
        if (delta is < -26 or > 25)
            throw new FormatException("mb_qp_delta is outside the supported 8-bit range.");
        return (previousQpY + delta + 52) % 52;
    }
}
