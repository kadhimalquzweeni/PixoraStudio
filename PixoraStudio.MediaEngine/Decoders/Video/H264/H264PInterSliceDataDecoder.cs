using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Traverses and reconstructs CAVLC P slices containing P_L0 inter macroblocks,
/// P_Skip runs and supported intra/PCM macroblocks. CABAC, FMO, field coding,
/// separate planes and unsupported chroma/bit-depth modes are rejected.
/// The caller supplies the active, already-modified RefPicList0 surfaces.
/// </summary>
public sealed class H264PInterSliceDataDecoder
{
    private readonly H264DecodedPictureSurface _destination;
    private readonly H264SliceHeaderReader _sliceHeaderReader = new();
    private readonly H264MacroblockHeaderReader _macroblockHeaderReader = new();
    private readonly H264PInterMacroblockReconstructor _macroblockReconstructor;
    private readonly H264MotionVectorPictureState _motionVectorState;
    private readonly H264CavlcNeighbourContext _lumaNeighbours = new();
    private readonly H264CavlcNeighbourContext _cbNeighbours = new();
    private readonly H264CavlcNeighbourContext _crNeighbours = new();
    private readonly H264IntraMacroblockReconstructor _intraReconstructor;
    private readonly H264IntraMacroblockSyntaxReader _intraSyntaxReader = new();
    private readonly H264IntraLumaResidualMacroblockReader _intraResidualReader = new();
    private readonly H264PcmMacroblockReader _pcmReader = new();

    public H264PInterSliceDataDecoder(
        H264DecodedPictureSurface destination,
        H264MotionVectorPictureState motionVectorState)
        : this(destination, motionVectorState, new H264PInterMacroblockReconstructor())
    {
    }

    public H264PInterSliceDataDecoder(
        H264DecodedPictureSurface destination,
        H264MotionVectorPictureState motionVectorState,
        H264PInterMacroblockReconstructor macroblockReconstructor)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(motionVectorState);
        ArgumentNullException.ThrowIfNull(macroblockReconstructor);
        _destination = destination;
        _motionVectorState = motionVectorState;
        _macroblockReconstructor = macroblockReconstructor;
        _intraReconstructor = new H264IntraMacroblockReconstructor(destination);
        if (motionVectorState.MacroblockWidth != destination.MacroblockWidth ||
            motionVectorState.MacroblockHeight != destination.MacroblockHeight)
            throw new ArgumentException("Motion-vector state dimensions must match the destination picture.", nameof(motionVectorState));
    }

    /// <summary>
    /// Clears the three picture-relative CAVLC neighbour contexts. Call once
    /// before decoding a new picture; do not call between slices of that picture.
    /// Motion-vector state is supplied separately and must also be new per picture.
    /// </summary>
    public void ResetPictureContexts()
    {
        _lumaNeighbours.Reset();
        _cbNeighbours.Reset();
        _crNeighbours.Reset();
        _intraReconstructor.ResetPictureContexts();
    }

    /// <summary>
    /// Decodes a P slice using the DPB to construct the active RefPicList0,
    /// including slice-header list modifications. The header is inspected
    /// once to build the list and parsed again by the main decode path.
    /// </summary>
    public H264PInterSliceDecodeResult Decode(
        H264NalUnit nalUnit,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        H264DecodedPictureBuffer decodedPictureBuffer,
        int sliceId)
    {
        ArgumentNullException.ThrowIfNull(nalUnit);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        ArgumentNullException.ThrowIfNull(decodedPictureBuffer);
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        ValidateConfiguration(sps, pps);

        H264SliceHeader slice = new H264SliceHeaderReader().Read(
            new H264BitReader(nalUnit.Rbsp), nalUnit, sps, pps);
        if (slice.SliceType != H264SliceType.P)
            throw new NotSupportedException("This decoder accepts P slices only.");
        if (slice.FieldPicFlag || slice.ColourPlaneId != 0 || slice.RedundantPicCnt is > 0)
            throw new NotSupportedException("Field, separate-plane and redundant P slices are not supported.");

        int maxFrameNum = 1 << checked((int)sps.Log2MaxFrameNumMinus4 + 4);
        int activeCount = checked((int)(slice.NumRefIdxL0ActiveMinus1 + 1));
        IReadOnlyList<H264DecodedPictureSurface> activeList0 =
            decodedPictureBuffer.BuildList0(
                checked((int)slice.FrameNum), maxFrameNum, activeCount,
                slice.RefPicList0Modifications);
        H264DecodedPictureBufferEntry[] entries = decodedPictureBuffer.Entries.ToArray();
        IReadOnlyList<H264ReferencePicture> referenceMetadata = activeList0
            .Select(surface => entries.Single(entry => ReferenceEquals(entry.Surface, surface)).Picture)
            .ToArray();
        return Decode(nalUnit, sps, pps, activeList0, sliceId, referenceMetadata) with
        {
            ReferenceList0 = referenceMetadata
        };
    }

    public H264PInterSliceDecodeResult Decode(
        H264NalUnit nalUnit,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        IReadOnlyList<H264DecodedPictureSurface> activeReferenceList0,
        int sliceId,
        IReadOnlyList<H264ReferencePicture>? referencePictureList0 = null)
    {
        ArgumentNullException.ThrowIfNull(nalUnit);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        ArgumentNullException.ThrowIfNull(activeReferenceList0);
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        ValidateConfiguration(sps, pps);

        H264BitReader reader = new(nalUnit.Rbsp);
        H264SliceHeader slice = _sliceHeaderReader.Read(reader, nalUnit, sps, pps);
        H264DeblockingSliceParameters deblockingParameters =
            H264DeblockingSliceParameters.FromSliceHeader(slice);
        _intraReconstructor.SetDeblockingSliceParameters(deblockingParameters);
        if (slice.SliceType != H264SliceType.P)
            throw new NotSupportedException("This decoder accepts P slices only.");
        if (slice.FieldPicFlag)
            throw new NotSupportedException("Field-coded P slices are not supported.");
        if (slice.ColourPlaneId != 0)
            throw new NotSupportedException("Separate-colour-plane P slices are not supported.");
        if (slice.RedundantPicCnt is > 0)
            throw new NotSupportedException("Redundant P slice decoding is not implemented.");

        int activeReferenceCount = checked((int)(slice.NumRefIdxL0ActiveMinus1 + 1));
        if (activeReferenceList0.Count != activeReferenceCount ||
            activeReferenceList0.Any(reference => reference is null))
            throw new ArgumentException(
                "The supplied RefPicList0 must contain exactly the active, non-null reference surfaces.",
                nameof(activeReferenceList0));

        uint pictureWidth = checked(sps.PicWidthInMbsMinus1 + 1);
        uint pictureHeight = checked(sps.PicHeightInMapUnitsMinus1 + 1);
        uint pictureMacroblockCount = checked(pictureWidth * pictureHeight);
        if (slice.FirstMacroblockInSlice >= pictureMacroblockCount)
            throw new FormatException("first_mb_in_slice is outside the decoded picture.");

        int qpY = checked(26 + pps.PicInitQpMinus26 + slice.SliceQpDelta);
        if (qpY is < 0 or > 51)
            throw new FormatException("The initial SliceQPY is outside the supported 8-bit range.");

        List<H264InterMacroblockReconstructionResult> results = [];
        List<uint> intraAddresses = [];
        int decodedCount = 0;
        uint address = slice.FirstMacroblockInSlice;
        while (reader.MoreRbspData())
        {
            if (address >= pictureMacroblockCount)
                throw new FormatException("P-slice data contains more macroblocks than remain in the picture.");

            H264MacroblockHeader header = _macroblockHeaderReader.ReadNext(
                reader, H264SliceType.P, address, pictureMacroblockCount,
                pps.EntropyCodingModeFlag);

            if (header.IsSkipped)
            {
                for (uint offset = 0; offset < header.SkippedMacroblockCount; offset++)
                {
                    uint skippedAddress = checked(address + offset);
                    int x = checked((int)(skippedAddress % pictureWidth));
                    int y = checked((int)(skippedAddress / pictureWidth));
                    H264MacroblockHeader oneSkipped = header with
                    {
                        MacroblockAddress = skippedAddress,
                        SkippedMacroblockCount = 1
                    };
                    results.Add(_macroblockReconstructor.DecodeSkippedAndReconstruct(
                        oneSkipped, activeReferenceList0, _motionVectorState,
                        _destination, x, y, sliceId, qpY, sps,
                        slice.PredictionWeights, referencePictureList0,
                        deblockingParameters));
                    RecordZeroResidualContexts(x, y, sliceId, sps);
                }
                decodedCount = checked(decodedCount + (int)header.SkippedMacroblockCount);
                address = checked(address + header.SkippedMacroblockCount);
                continue;
            }

            int macroblockX = checked((int)(address % pictureWidth));
            int macroblockY = checked((int)(address / pictureWidth));
            if (header.IsPcm)
            {
                H264PcmMacroblock pcm = _pcmReader.Read(reader, header, sps, pps);
                _intraReconstructor.ReconstructPcm(
                    header, pcm, macroblockX, macroblockY, sliceId, sps, pps, qpY: qpY);
                RecordPcmResidualContexts(macroblockX, macroblockY, sliceId, sps);
                _motionVectorState.RecordUnavailableRegion(
                    macroblockX * 4, macroblockY * 4, 4, 4, sliceId);
                intraAddresses.Add(address);
            }
            else if (header.IsIntra)
            {
                H264IntraMacroblockSyntax syntax = _intraSyntaxReader.Read(reader, header, sps, pps);
                int macroblockQp = syntax.MacroblockQpDelta is int delta
                    ? UpdateQpY(qpY, delta)
                    : qpY;
                H264IntraLumaResidualMacroblock residual = _intraResidualReader.Read(
                    reader, header, syntax, sps,
                    macroblockX, macroblockY, sliceId,
                    _lumaNeighbours, _cbNeighbours, _crNeighbours);
                if (header.IsIntra16x16)
                    _intraReconstructor.ReconstructIntra16x16(
                        macroblockX, macroblockY, sliceId, header, syntax,
                        residual, macroblockQp, sps, pps);
                else if (syntax.TransformSize8X8Flag == true)
                    _intraReconstructor.ReconstructIntraNxN8x8(
                        macroblockX, macroblockY, sliceId, header, syntax,
                        residual, macroblockQp, sps, pps);
                else
                    _intraReconstructor.ReconstructIntraNxN4x4(
                        macroblockX, macroblockY, sliceId, header, syntax,
                        residual, macroblockQp, sps, pps);
                qpY = macroblockQp;
                _motionVectorState.RecordUnavailableRegion(
                    macroblockX * 4, macroblockY * 4, 4, 4, sliceId);
                intraAddresses.Add(address);
            }
            else
            {
                H264InterMacroblockReconstructionResult reconstructed =
                    _macroblockReconstructor.DecodeAndReconstruct(
                        reader, header, slice.NumRefIdxL0ActiveMinus1,
                        activeReferenceList0, _motionVectorState, _destination,
                        macroblockX, macroblockY, sliceId, qpY, sps, pps,
                        _lumaNeighbours, _cbNeighbours, _crNeighbours,
                        slice.PredictionWeights, referencePictureList0,
                        deblockingParameters);
                qpY = reconstructed.QpY;
                results.Add(reconstructed);
            }
            decodedCount++;
            address++;
        }

        if (decodedCount == 0)
            throw new FormatException("The P slice contains no macroblock data.");

        reader.ConsumeRbspTrailingBits();
        return new H264PInterSliceDecodeResult(
            slice, decodedCount, qpY, reader.PositionBits, results)
        {
            IntraMacroblockAddresses = intraAddresses,
            ReferenceList0 = referencePictureList0 ?? []
        };
    }

    private void RecordZeroResidualContexts(
        int macroblockX, int macroblockY, int sliceId, H264SequenceParameterSet sps)
    {
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            _lumaNeighbours.RecordBlock(macroblockX * 4 + x, macroblockY * 4 + y, sliceId, 0);
        int chromaHeight = sps.ChromaFormatIdc == 1 ? 2 : 4;
        for (int y = 0; y < chromaHeight; y++)
        for (int x = 0; x < 2; x++)
        {
            _cbNeighbours.RecordBlock(macroblockX * 2 + x, macroblockY * chromaHeight + y, sliceId, 0);
            _crNeighbours.RecordBlock(macroblockX * 2 + x, macroblockY * chromaHeight + y, sliceId, 0);
        }
    }

    private void RecordPcmResidualContexts(
        int macroblockX, int macroblockY, int sliceId, H264SequenceParameterSet sps)
    {
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            _lumaNeighbours.RecordBlock(macroblockX * 4 + x, macroblockY * 4 + y, sliceId, 16);
        int chromaHeight = sps.ChromaFormatIdc == 1 ? 2 : 4;
        for (int y = 0; y < chromaHeight; y++)
        for (int x = 0; x < 2; x++)
        {
            _cbNeighbours.RecordBlock(macroblockX * 2 + x, macroblockY * chromaHeight + y, sliceId, 16);
            _crNeighbours.RecordBlock(macroblockX * 2 + x, macroblockY * chromaHeight + y, sliceId, 16);
        }
    }

    private static int UpdateQpY(int previousQpY, int delta)
    {
        if (delta is < -26 or > 25)
            throw new FormatException("mb_qp_delta is outside the supported 8-bit range.");
        return (previousQpY + delta + 52) % 52;
    }

    private void ValidateConfiguration(
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (pps.EntropyCodingModeFlag)
            throw new NotSupportedException("CABAC P-slice decoding is not implemented.");
        if (pps.NumSliceGroupsMinus1 != 0)
            throw new NotSupportedException("FMO slice groups are not supported by the P-slice decoder.");
        if (!sps.FrameMbsOnlyFlag || sps.MbAdaptiveFrameFieldFlag)
            throw new NotSupportedException("Only frame-coded P slices are supported.");
        if (sps.SeparateColourPlaneFlag || sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Only non-separate 4:2:0 and 4:2:2 P slices are supported.");
        if (sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0)
            throw new NotSupportedException("Only 8-bit P slices are supported.");
        if (sps.PicWidthInMbsMinus1 + 1 != (uint)_destination.MacroblockWidth ||
            sps.PicHeightInMapUnitsMinus1 + 1 != (uint)_destination.MacroblockHeight ||
            sps.ChromaFormatIdc != _destination.ChromaFormatIdc)
            throw new ArgumentException("The SPS does not describe the destination picture surface.", nameof(sps));
    }
}
