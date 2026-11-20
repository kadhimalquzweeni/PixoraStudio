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
        H264PredictionWeightTable? predictionWeights = null,
        IReadOnlyList<H264ReferencePicture>? referencePictureList0 = null,
        H264DeblockingSliceParameters? deblockingParameters = null)
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
        if (referencePictureList0 is not null &&
            referencePictureList0.Count != referenceList0.Count)
            throw new ArgumentException("Reference-picture metadata must match RefPicList0.", nameof(referencePictureList0));
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
            lumaNeighbours, chromaCbNeighbours, chromaCrNeighbours,
            transformSize8x8Flag: residualSyntax.TransformSize8X8Flag);

        H264InterMacroblockPrediction[] macroblockLocalPredictions = partitions
            .Select(item => item with
            {
                Partition = item.Partition with
                {
                    X = item.Partition.X - (macroblockX * 4),
                    Y = item.Partition.Y - (macroblockY * 4)
                }
            })
            .ToArray();
        H264MotionCompensatedBlock prediction = H264InterPredictionAssembler.Assemble(
            macroblockLocalPredictions, sps.ChromaFormatIdc);
        H264MotionCompensatedBlock reconstructed =
            H264InterMacroblockResidualReconstructor.Reconstruct(
                prediction, residual, qpY, sps, pps);

        destination.WriteMacroblock(
            macroblockX, macroblockY, sliceId, false,
            reconstructed.Luma, reconstructed.Cb, reconstructed.Cr);
        if (referencePictureList0 is not null)
            AttachDeblockingMetadata(
                destination, motionVectorState, referencePictureList0,
                residual, qpY, macroblockX, macroblockY, sps, pps,
                deblockingParameters ?? new H264DeblockingSliceParameters(0, 0, 0));

        return new H264InterMacroblockReconstructionResult(
            header.MacroblockAddress, qpY, residualSyntax.CodedBlockPattern,
            partitions, prediction, reconstructed);
    }


    /// <summary>
    /// Reconstructs a CABAC P inter macroblock from syntax and residual data
    /// that have already been decoded by the CABAC readers. The same
    /// prediction assembly, inverse transform, surface commit and deblocking
    /// metadata path is used as the CAVLC implementation.
    /// </summary>
    public H264InterMacroblockReconstructionResult ReconstructCabac(
        H264MacroblockHeader header,
        IReadOnlyList<H264InterMacroblockPrediction> partitions,
        H264InterMacroblockResidualSyntax residualSyntax,
        H264InterResidualMacroblock residual,
        H264MotionVectorPictureState motionVectorState,
        IReadOnlyList<H264ReferencePicture> referencePictureList0,
        H264DecodedPictureSurface destination,
        int macroblockX,
        int macroblockY,
        int sliceId,
        int previousQpY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        H264DeblockingSliceParameters? deblockingParameters = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(partitions);
        ArgumentNullException.ThrowIfNull(residualSyntax);
        ArgumentNullException.ThrowIfNull(residual);
        ArgumentNullException.ThrowIfNull(motionVectorState);
        ArgumentNullException.ThrowIfNull(referencePictureList0);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (!pps.EntropyCodingModeFlag)
            throw new ArgumentException("The PPS must enable CABAC.", nameof(pps));
        if (header.IsSkipped || header.IsIntra || header.IsPcm)
            throw new ArgumentException("A coded inter P macroblock header is required.", nameof(header));
        if (partitions.Count == 0)
            throw new ArgumentException("At least one inter prediction partition is required.", nameof(partitions));
        if (referencePictureList0.Count == 0)
            throw new ArgumentException("RefPicList0 metadata cannot be empty.", nameof(referencePictureList0));
        if (previousQpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(previousQpY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if ((uint)macroblockX >= (uint)destination.MacroblockWidth ||
            (uint)macroblockY >= (uint)destination.MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (header.MacroblockAddress !=
            checked((uint)(macroblockY * destination.MacroblockWidth + macroblockX)))
            throw new FormatException("The macroblock address does not match its picture coordinates.");
        if (residual.MacroblockAddress != header.MacroblockAddress)
            throw new FormatException("The residual macroblock address does not match its header.");
        if (destination.IsMacroblockDecoded(macroblockX, macroblockY))
            throw new InvalidOperationException("The destination macroblock has already been decoded.");
        if (motionVectorState.MacroblockWidth != destination.MacroblockWidth ||
            motionVectorState.MacroblockHeight != destination.MacroblockHeight)
            throw new ArgumentException("Motion-vector state dimensions must match the destination.", nameof(motionVectorState));
        if (partitions.Any(partition =>
                partition.Partition.ReferenceIndex < 0 ||
                partition.Partition.ReferenceIndex >= referencePictureList0.Count))
            throw new ArgumentException(
                "Reference-picture metadata must cover every selected RefPicList0 index.",
                nameof(referencePictureList0));

        int qpY = residualSyntax.MacroblockQpDelta is int delta
            ? UpdateQpY(previousQpY, delta)
            : previousQpY;
        H264InterMacroblockPrediction[] macroblockLocalPredictions = partitions
            .Select(item => item with
            {
                Partition = item.Partition with
                {
                    X = item.Partition.X - (macroblockX * 4),
                    Y = item.Partition.Y - (macroblockY * 4)
                }
            })
            .ToArray();
        H264MotionCompensatedBlock prediction = H264InterPredictionAssembler.Assemble(
            macroblockLocalPredictions, sps.ChromaFormatIdc);
        H264MotionCompensatedBlock reconstructed =
            H264InterMacroblockResidualReconstructor.Reconstruct(
                prediction, residual, qpY, sps, pps);

        destination.WriteMacroblock(
            macroblockX, macroblockY, sliceId, false,
            reconstructed.Luma, reconstructed.Cb, reconstructed.Cr);
        AttachDeblockingMetadata(
            destination, motionVectorState, referencePictureList0,
            residual, qpY, macroblockX, macroblockY, sps, pps,
            deblockingParameters ?? new H264DeblockingSliceParameters(0, 0, 0));

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
        H264PredictionWeightTable? predictionWeights = null,
        IReadOnlyList<H264ReferencePicture>? referencePictureList0 = null,
        H264DeblockingSliceParameters? deblockingParameters = null,
        H264PictureParameterSetHeader? pps = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(referenceList0);
        ArgumentNullException.ThrowIfNull(motionVectorState);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sps);

        if (!header.IsSkipped || header.SkippedMacroblockCount != 1)
            throw new ArgumentException("A single P_Skip macroblock header is required.", nameof(header));
        if (referencePictureList0 is not null &&
            referencePictureList0.Count != referenceList0.Count)
            throw new ArgumentException("Reference-picture metadata must match RefPicList0.", nameof(referencePictureList0));
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
        H264InterMacroblockPrediction macroblockLocalPrediction = partition with
        {
            Partition = partition.Partition with
            {
                X = partition.Partition.X - (macroblockX * 4),
                Y = partition.Partition.Y - (macroblockY * 4)
            }
        };
        H264MotionCompensatedBlock prediction = H264InterPredictionAssembler.Assemble(
            [macroblockLocalPrediction], sps.ChromaFormatIdc);

        destination.WriteMacroblock(
            macroblockX, macroblockY, sliceId, false,
            prediction.Luma, prediction.Cb, prediction.Cr);
        if (referencePictureList0 is not null)
            AttachDeblockingMetadata(
                destination, motionVectorState, referencePictureList0,
                null, currentQpY, macroblockX, macroblockY, sps,
                pps, deblockingParameters ?? new H264DeblockingSliceParameters(0, 0, 0));

        H264CodedBlockPattern emptyPattern = new(0, 0, 0, sps.ChromaFormatIdc);
        return new H264InterMacroblockReconstructionResult(
            header.MacroblockAddress, currentQpY, emptyPattern,
            [partition], prediction, prediction);
    }

    private static void AttachDeblockingMetadata(
        H264DecodedPictureSurface destination,
        H264MotionVectorPictureState motionVectorState,
        IReadOnlyList<H264ReferencePicture> referencePictureList0,
        H264InterResidualMacroblock? residual,
        int qpY,
        int macroblockX,
        int macroblockY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader? pps,
        H264DeblockingSliceParameters deblockingParameters)
    {
        if (referencePictureList0.Count == 0)
            throw new FormatException("Deblocking metadata requires the active P RefPicList0.");
        deblockingParameters.Validate();

        bool[] lumaNonZero = new bool[16];
        bool[] cbNonZero = new bool[sps.ChromaFormatIdc == 1 ? 4 : 8];
        bool[] crNonZero = new bool[cbNonZero.Length];
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

            H264IntraChromaResidualComponent cb = residual.ChromaComponents.Single(c => c.ComponentId == 1);
            H264IntraChromaResidualComponent cr = residual.ChromaComponents.Single(c => c.ComponentId == 2);
            FillChromaNonZero(cb, cbNonZero);
            FillChromaNonZero(cr, crNonZero);
        }

        H264DeblockingBlockMetadata[] lumaBlocks = new H264DeblockingBlockMetadata[16];
        for (int blockY = 0; blockY < 4; blockY++)
        for (int blockX = 0; blockX < 4; blockX++)
        {
            if (!motionVectorState.TryGetStoredMotionVector(
                macroblockX * 4 + blockX, macroblockY * 4 + blockY,
                out H264MotionVectorCandidate candidate))
                throw new FormatException("A reconstructed P inter block is missing its stored motion vector.");
            if ((uint)candidate.ReferenceIndex >= (uint)referencePictureList0.Count)
                throw new FormatException("A P inter block reference index is outside RefPicList0.");
            lumaBlocks[blockY * 4 + blockX] = new H264DeblockingBlockMetadata(
                false, lumaNonZero[blockY * 4 + blockX],
                referencePictureList0[candidate.ReferenceIndex], candidate.MotionVector,
                null, null, candidate.ReferenceIndex, null);
        }

        int chromaRows = sps.ChromaFormatIdc == 1 ? 2 : 4;
        H264DeblockingBlockMetadata[] cbBlocks = new H264DeblockingBlockMetadata[chromaRows * 2];
        H264DeblockingBlockMetadata[] crBlocks = new H264DeblockingBlockMetadata[chromaRows * 2];
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

        int qpCb = pps is null ? qpY :
            H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(qpY, 1, sps, pps);
        int qpCr = pps is null ? qpY :
            H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(qpY, 2, sps, pps);
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
        ValidateSurface(destination, x, y, sliceId, sps);
        if (header.MacroblockAddress != checked((uint)(y * destination.MacroblockWidth + x)))
            throw new FormatException("The macroblock header address does not match its picture coordinates.");
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
