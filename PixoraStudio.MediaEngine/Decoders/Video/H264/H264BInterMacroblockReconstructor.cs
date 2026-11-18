using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reconstructs one frame-coded B inter macroblock by combining parsed
/// prediction syntax, independent list motion vectors, motion compensation,
/// weighted bi-prediction, CAVLC residuals and decoded-surface commit.
/// Spatial and temporal direct are supported; collocated motion metadata is
/// supplied through a region-based callback.
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
        H264PredictionWeightTable? predictionWeights = null,
        Func<int, int, int, int, H264TemporalDirectPredictionContext?>? collocatedContextProvider = null,
        H264DeblockingSliceParameters? deblockingParameters = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(header);
        ValidateContext(destination, macroblockX, macroblockY, sliceId, previousQpY, sps, pps);
        if (header.IsSkipped || header.IsIntra || header.IsPcm)
            throw new ArgumentException("A coded inter B macroblock header is required.", nameof(header));
        if (pps.WeightedBipredIdc == 1 && predictionWeights is null)
            throw new ArgumentNullException(nameof(predictionWeights));
        if (referenceList0.Count != checked((int)(numRefIdxL0ActiveMinus1 + 1)) ||
            referenceList1.Count != checked((int)(numRefIdxL1ActiveMinus1 + 1)))
            throw new ArgumentException("Decoded reference surfaces must match the active B reference-list counts.");
        if (header.MacroblockAddress != checked((uint)(macroblockY * destination.MacroblockWidth + macroblockX)))
            throw new FormatException("The B macroblock header address does not match its picture coordinates.");

        H264BMacroblockSyntax syntax = _syntaxReader.Read(
            reader, header, numRefIdxL0ActiveMinus1, numRefIdxL1ActiveMinus1);
        IReadOnlyList<H264ReconstructedBInterSubPartition> partitions =
            _motionReconstructor.Reconstruct(
                syntax, motionVectorStateL0, motionVectorStateL1,
                macroblockX, macroblockY, sliceId,
                checked((int)(numRefIdxL0ActiveMinus1 + 1)),
                checked((int)(numRefIdxL1ActiveMinus1 + 1)),
                directSpatialPrediction, sps.Direct8X8InferenceFlag, collocatedContextProvider);

        IReadOnlyList<H264BInterMacroblockPrediction> predictions =
            _predictionDecoder.DecodeAndPredict(
                partitions, referenceList0, referenceList1, referencePictureLists,
                sps.ChromaFormatIdc, currentPictureOrderCount,
                pps.WeightedBipredIdc, predictionWeights);
        H264BInterMacroblockPrediction[] macroblockLocalPredictions = predictions
            .Select(item => item with
            {
                Partition = item.Partition with
                {
                    X = item.Partition.X - (macroblockX * 4),
                    Y = item.Partition.Y - (macroblockY * 4)
                }
            })
            .ToArray();
        H264MotionCompensatedBlock prediction =
            H264BInterMacroblockPredictionDecoder.Assemble(
                macroblockLocalPredictions, sps.ChromaFormatIdc);

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
            lumaNeighbours, chromaCbNeighbours, chromaCrNeighbours,
            transformSize8x8Flag: residualSyntax.TransformSize8X8Flag);
        H264MotionCompensatedBlock reconstructed =
            H264InterMacroblockResidualReconstructor.Reconstruct(
                prediction, residual, qpY, sps, pps);

        destination.WriteMacroblock(
            macroblockX, macroblockY, sliceId, false,
            reconstructed.Luma, reconstructed.Cb, reconstructed.Cr);
        AttachDeblockingMetadata(
            destination, motionVectorStateL0, motionVectorStateL1,
            referencePictureLists, residual, qpY,
            macroblockX, macroblockY, sps, pps,
            deblockingParameters ?? new H264DeblockingSliceParameters(0, 0, 0));
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
        H264PictureParameterSetHeader pps,
        H264PredictionWeightTable? predictionWeights = null,
        Func<int, int, int, int, H264TemporalDirectPredictionContext?>? collocatedContextProvider = null,
        H264DeblockingSliceParameters? deblockingParameters = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ValidateContext(destination, macroblockX, macroblockY, sliceId, currentQpY, sps, pps);
        if (!header.IsSkipped || header.SkippedMacroblockCount != 1)
            throw new ArgumentException("A single B_Skip macroblock header is required.", nameof(header));
        if (header.MacroblockAddress != checked((uint)(macroblockY * destination.MacroblockWidth + macroblockX)))
            throw new FormatException("The B_Skip header address does not match its picture coordinates.");
        if (collocatedContextProvider is null)
            throw new NotSupportedException("B_Skip direct prediction requires a collocated-picture metadata provider.");
        if (pps.WeightedBipredIdc == 1 && predictionWeights is null)
            throw new ArgumentNullException(nameof(predictionWeights));
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
                directSpatialPrediction, sps.Direct8X8InferenceFlag, collocatedContextProvider);
        IReadOnlyList<H264BInterMacroblockPrediction> predictions =
            _predictionDecoder.DecodeAndPredict(
                partitions, referenceList0, referenceList1, referencePictureLists,
                sps.ChromaFormatIdc, currentPictureOrderCount,
                pps.WeightedBipredIdc, predictionWeights);
        H264BInterMacroblockPrediction[] macroblockLocalPredictions = predictions
            .Select(item => item with
            {
                Partition = item.Partition with
                {
                    X = item.Partition.X - (macroblockX * 4),
                    Y = item.Partition.Y - (macroblockY * 4)
                }
            })
            .ToArray();
        H264MotionCompensatedBlock prediction =
            H264BInterMacroblockPredictionDecoder.Assemble(
                macroblockLocalPredictions, sps.ChromaFormatIdc);
        destination.WriteMacroblock(
            macroblockX, macroblockY, sliceId, false,
            prediction.Luma, prediction.Cb, prediction.Cr);
        AttachDeblockingMetadata(
            destination, motionVectorStateL0, motionVectorStateL1,
            referencePictureLists, null, currentQpY,
            macroblockX, macroblockY, sps, pps,
            deblockingParameters ?? new H264DeblockingSliceParameters(0, 0, 0));

        return new H264BInterMacroblockReconstructionResult(
            header.MacroblockAddress, currentQpY,
            new H264CodedBlockPattern(0, 0, 0, sps.ChromaFormatIdc),
            true, partitions, prediction, prediction);
    }

    private static void AttachDeblockingMetadata(
        H264DecodedPictureSurface destination,
        H264MotionVectorPictureState motionVectorStateL0,
        H264MotionVectorPictureState motionVectorStateL1,
        H264BReferencePictureLists referencePictureLists,
        H264InterResidualMacroblock? residual,
        int qpY,
        int macroblockX,
        int macroblockY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        H264DeblockingSliceParameters deblockingParameters)
    {
        if (referencePictureLists.List0.Count == 0 ||
            referencePictureLists.List1.Count == 0)
            throw new FormatException("B-slice deblocking metadata requires both active reference lists.");
        deblockingParameters.Validate();

        bool[] lumaNonZero = new bool[16];
        int chromaBlockCount = sps.ChromaFormatIdc == 1 ? 4 : 8;
        bool[] cbNonZero = new bool[chromaBlockCount];
        bool[] crNonZero = new bool[chromaBlockCount];
        if (residual is not null)
        {
            if (residual.TransformSize8X8Flag)
            {
                foreach (H264Intra8x8LumaResidualBlock block in residual.Luma8x8Blocks)
                {
                    if (!block.CoefficientsInScanOrder.Any(level => level != 0))
                        continue;
                    for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                        lumaNonZero[(block.BlockY * 2 + dy) * 4 + block.BlockX * 2 + dx] = true;
                }
            }
            else
            {
                foreach (H264IntraLumaResidualBlock block in residual.LumaBlocks)
                    lumaNonZero[block.BlockY * 4 + block.BlockX] =
                        block.Residual.TotalCoefficients > 0;
            }

            FillChromaNonZero(
                residual.ChromaComponents.Single(component => component.ComponentId == 1),
                cbNonZero);
            FillChromaNonZero(
                residual.ChromaComponents.Single(component => component.ComponentId == 2),
                crNonZero);
        }

        H264DeblockingBlockMetadata[] lumaBlocks = new H264DeblockingBlockMetadata[16];
        for (int blockY = 0; blockY < 4; blockY++)
        for (int blockX = 0; blockX < 4; blockX++)
        {
            int x = macroblockX * 4 + blockX;
            int y = macroblockY * 4 + blockY;
            H264ReferencePicture? reference0 = null;
            H264ReferencePicture? reference1 = null;
            H264MotionVector? motion0 = null;
            H264MotionVector? motion1 = null;
            if (motionVectorStateL0.TryGetStoredMotionVector(x, y, out H264MotionVectorCandidate candidate0))
            {
                if ((uint)candidate0.ReferenceIndex >= (uint)referencePictureLists.List0.Count)
                    throw new FormatException("A B inter list-0 reference index is outside RefPicList0.");
                reference0 = referencePictureLists.List0[candidate0.ReferenceIndex];
                motion0 = candidate0.MotionVector;
            }
            if (motionVectorStateL1.TryGetStoredMotionVector(x, y, out H264MotionVectorCandidate candidate1))
            {
                if ((uint)candidate1.ReferenceIndex >= (uint)referencePictureLists.List1.Count)
                    throw new FormatException("A B inter list-1 reference index is outside RefPicList1.");
                reference1 = referencePictureLists.List1[candidate1.ReferenceIndex];
                motion1 = candidate1.MotionVector;
            }

            lumaBlocks[blockY * 4 + blockX] = new H264DeblockingBlockMetadata(
                false, lumaNonZero[blockY * 4 + blockX],
                reference0, motion0, reference1, motion1);
        }

        int chromaRows = sps.ChromaFormatIdc == 1 ? 2 : 4;
        H264DeblockingBlockMetadata[] cbBlocks = new H264DeblockingBlockMetadata[chromaBlockCount];
        H264DeblockingBlockMetadata[] crBlocks = new H264DeblockingBlockMetadata[chromaBlockCount];
        for (int blockY = 0; blockY < chromaRows; blockY++)
        for (int blockX = 0; blockX < 2; blockX++)
        {
            int lumaX = blockX * 2;
            int lumaY = sps.ChromaFormatIdc == 1 ? blockY * 2 : blockY;
            H264DeblockingBlockMetadata luma = lumaBlocks[lumaY * 4 + lumaX];
            int index = blockY * 2 + blockX;
            cbBlocks[index] = luma with { HasNonZeroCoefficients = cbNonZero[index] };
            crBlocks[index] = luma with { HasNonZeroCoefficients = crNonZero[index] };
        }

        int qpCb = H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(qpY, 1, sps, pps);
        int qpCr = H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(qpY, 2, sps, pps);
        destination.SetDeblockingMetadata(macroblockX, macroblockY,
            new H264DeblockingMacroblockMetadata(
                qpY, qpCb, qpCr,
                deblockingParameters.DisableDeblockingFilterIdc,
                deblockingParameters.AlphaOffsetDiv2,
                deblockingParameters.BetaOffsetDiv2,
                residual?.TransformSize8X8Flag ?? false,
                lumaBlocks, cbBlocks, crBlocks));
    }

    private static void FillChromaNonZero(
        H264IntraChromaResidualComponent component,
        bool[] flags)
    {
        bool dcNonZero = component.Dc is { TotalCoefficients: > 0 };
        for (int i = 0; i < component.Blocks.Count; i++)
            flags[i] = dcNonZero ||
                component.Blocks[i] is { Residual.TotalCoefficients: > 0 };
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
