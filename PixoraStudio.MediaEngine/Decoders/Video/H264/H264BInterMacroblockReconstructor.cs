using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reconstructs one frame-coded B inter macroblock by combining parsed
/// prediction syntax, independent list motion vectors, motion compensation,
/// weighted bi-prediction, CAVLC residuals and decoded-surface commit.
/// Spatial direct is supported; temporal direct requires collocated metadata.
/// </summary>
public sealed class H264BInterMacroblockReconstructor
{
    private readonly H264BMacroblockSyntaxReader _syntaxReader = new();
    private readonly H264BInterMacroblockMotionVectorReconstructor _motionReconstructor;
    private readonly H264BInterMacroblockPredictionDecoder _predictionDecoder;
    private readonly H264InterMacroblockResidualSyntaxReader _residualSyntaxReader = new();
    private readonly H264InterResidualMacroblockReader _residualReader = new();

    public H264BInterMacroblockReconstructor()
        : this(new H264BInterMacroblockMotionVectorReconstructor(),
            new H264BInterMacroblockPredictionDecoder())
    {
    }

    public H264BInterMacroblockReconstructor(
        H264BInterMacroblockMotionVectorReconstructor motionReconstructor,
        H264BInterMacroblockPredictionDecoder predictionDecoder)
    {
        ArgumentNullException.ThrowIfNull(motionReconstructor);
        ArgumentNullException.ThrowIfNull(predictionDecoder);
        _motionReconstructor = motionReconstructor;
        _predictionDecoder = predictionDecoder;
    }

    public H264BInterMacroblockReconstructionResult DecodeAndReconstruct(
        H264BitReader reader,
        H264MacroblockHeader header,
        uint numRefIdxL0ActiveMinus1,
        uint numRefIdxL1ActiveMinus1,
        IReadOnlyList<H264DecodedPictureSurface> referenceList0,
        IReadOnlyList<H264DecodedPictureSurface> referenceList1,
        H264BReferencePictureLists referencePictureLists,
        H264MotionVectorPictureState motionVectorStateL0,
        H264MotionVectorPictureState motionVectorStateL1,
        H264DecodedPictureSurface destination,
        int macroblockX,
        int macroblockY,
        int sliceId,
        int previousQpY,
        int currentPictureOrderCount,
        bool directSpatialPrediction,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        H264CavlcNeighbourContext lumaNeighbours,
        H264CavlcNeighbourContext chromaCbNeighbours,
        H264CavlcNeighbourContext chromaCrNeighbours,
        H264PredictionWeightTable? predictionWeights = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(header);
        ValidateContext(destination, macroblockX, macroblockY, sliceId, previousQpY, sps, pps);
        if (header.IsSkipped || header.IsIntra || header.IsPcm)
            throw new ArgumentException("A coded inter B macroblock header is required.", nameof(header));
        if (pps.WeightedBipredIdc == 1 && predictionWeights is null)
            throw new ArgumentNullException(nameof(predictionWeights));

        H264BMacroblockSyntax syntax = _syntaxReader.Read(
            reader, header, numRefIdxL0ActiveMinus1, numRefIdxL1ActiveMinus1);
        IReadOnlyList<H264ReconstructedBInterSubPartition> partitions =
            _motionReconstructor.Reconstruct(
                syntax, motionVectorStateL0, motionVectorStateL1,
                macroblockX, macroblockY, sliceId,
                checked((int)(numRefIdxL0ActiveMinus1 + 1)),
                checked((int)(numRefIdxL1ActiveMinus1 + 1)),
                directSpatialPrediction, sps.Direct8X8InferenceFlag);

        IReadOnlyList<H264BInterMacroblockPrediction> predictions =
            _predictionDecoder.DecodeAndPredict(
                partitions, referenceList0, referenceList1, referencePictureLists,
                sps.ChromaFormatIdc, currentPictureOrderCount,
                pps.WeightedBipredIdc, predictionWeights);
        H264MotionCompensatedBlock prediction =
            H264BInterMacroblockPredictionDecoder.Assemble(predictions, sps.ChromaFormatIdc);

        bool noSubMbPartSizeLessThan8x8Flag = partitions.All(partition =>
            partition.Width >= 2 && partition.Height >= 2);
        H264InterMacroblockResidualSyntax residualSyntax = _residualSyntaxReader.Read(
            reader, header, sps, pps, noSubMbPartSizeLessThan8x8Flag);
        int qpY = residualSyntax.MacroblockQpDelta is int delta
            ? UpdateQpY(previousQpY, delta)
            : previousQpY;
        H264InterResidualMacroblock residual = _residualReader.Read(
            reader, header, residualSyntax.CodedBlockPattern, sps,
            macroblockX, macroblockY, sliceId,
            lumaNeighbours, chromaCbNeighbours, chromaCrNeighbours);
        H264MotionCompensatedBlock reconstructed =
            H264InterMacroblockResidualReconstructor.Reconstruct(
                prediction, residual, qpY, sps, pps);

        destination.WriteMacroblock(
            macroblockX, macroblockY, sliceId, false,
            reconstructed.Luma, reconstructed.Cb, reconstructed.Cr);
        return new H264BInterMacroblockReconstructionResult(
            header.MacroblockAddress, qpY, residualSyntax.CodedBlockPattern,
            false, partitions, prediction, reconstructed);
    }

    /// <summary>Reconstructs one B_Skip macroblock using the slice direct mode.</summary>
    public H264BInterMacroblockReconstructionResult DecodeSkippedAndReconstruct(
        H264MacroblockHeader header,
        IReadOnlyList<H264DecodedPictureSurface> referenceList0,
        IReadOnlyList<H264DecodedPictureSurface> referenceList1,
        H264BReferencePictureLists referencePictureLists,
        H264MotionVectorPictureState motionVectorStateL0,
        H264MotionVectorPictureState motionVectorStateL1,
        H264DecodedPictureSurface destination,
        int macroblockX,
        int macroblockY,
        int sliceId,
        int currentQpY,
        int currentPictureOrderCount,
        bool directSpatialPrediction,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(header);
        ValidateContext(destination, macroblockX, macroblockY, sliceId, currentQpY, sps, pps);
        if (!header.IsSkipped || header.SkippedMacroblockCount != 1)
            throw new ArgumentException("A single B_Skip macroblock header is required.", nameof(header));
        if (!directSpatialPrediction)
            throw new NotSupportedException("Temporal B_Skip prediction requires collocated-picture motion metadata.");
        if (destination.IsMacroblockDecoded(macroblockX, macroblockY))
            throw new InvalidOperationException("The destination macroblock has already been decoded.");

        H264BMacroblockSyntax directSyntax = new(
            0, false,
            [new H264BPredictionPartition(0, 0, 16, 16,
                H264BPredictionDirection.Direct, null, null,
                [new H264BSubPartition(0, 0, 16, 16,
                    H264BPredictionDirection.Direct, null, null)])]);
        IReadOnlyList<H264ReconstructedBInterSubPartition> partitions =
            _motionReconstructor.Reconstruct(
                directSyntax, motionVectorStateL0, motionVectorStateL1,
                macroblockX, macroblockY, sliceId,
                referenceList0.Count, referenceList1.Count,
                true, sps.Direct8X8InferenceFlag);
        IReadOnlyList<H264BInterMacroblockPrediction> predictions =
            _predictionDecoder.DecodeAndPredict(
                partitions, referenceList0, referenceList1, referencePictureLists,
                sps.ChromaFormatIdc, currentPictureOrderCount,
                pps.WeightedBipredIdc);
        H264MotionCompensatedBlock prediction =
            H264BInterMacroblockPredictionDecoder.Assemble(predictions, sps.ChromaFormatIdc);
        destination.WriteMacroblock(
            macroblockX, macroblockY, sliceId, false,
            prediction.Luma, prediction.Cb, prediction.Cr);

        return new H264BInterMacroblockReconstructionResult(
            header.MacroblockAddress, currentQpY,
            new H264CodedBlockPattern(0, 0, 0, sps.ChromaFormatIdc),
            true, partitions, prediction, prediction);
    }

    private static void ValidateContext(
        H264DecodedPictureSurface destination,
        int x,
        int y,
        int sliceId,
        int qpY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        if ((uint)x >= (uint)destination.MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)destination.MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (qpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(qpY));
        if (pps.EntropyCodingModeFlag)
            throw new NotSupportedException("CABAC B-slice reconstruction is not implemented.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (!sps.FrameMbsOnlyFlag || sps.SeparateColourPlaneFlag ||
            sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0 ||
            sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("B-slice reconstruction supports frame-only 8-bit non-separate 4:2:0 and 4:2:2.");
        if (sps.PicWidthInMbsMinus1 + 1 != (uint)destination.MacroblockWidth ||
            sps.PicHeightInMapUnitsMinus1 + 1 != (uint)destination.MacroblockHeight ||
            sps.ChromaFormatIdc != destination.ChromaFormatIdc)
            throw new ArgumentException("The SPS does not describe the destination picture surface.", nameof(sps));
        uint address = checked((uint)(y * destination.MacroblockWidth + x));
        if (destination.IsMacroblockDecoded(x, y))
            throw new InvalidOperationException("The destination macroblock has already been decoded.");
    }

    private static int UpdateQpY(int previousQpY, int delta)
    {
        if (delta is < -26 or > 25)
            throw new FormatException("mb_qp_delta is outside the supported 8-bit range.");
        return (previousQpY + delta + 52) % 52;
    }
}
