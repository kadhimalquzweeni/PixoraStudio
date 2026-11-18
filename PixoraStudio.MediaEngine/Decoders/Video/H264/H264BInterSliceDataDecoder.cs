using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Traverses frame-coded CAVLC B slices, expands B_Skip runs, constructs both
/// reference lists from the DPB, and reconstructs explicit and provider-backed
/// spatial/temporal direct B inter macroblocks plus intra/PCM blocks.
/// </summary>
public sealed class H264BInterSliceDataDecoder
{
    private readonly H264DecodedPictureSurface _destination;
    private readonly H264MotionVectorPictureState _motionVectorStateL0;
    private readonly H264MotionVectorPictureState _motionVectorStateL1;
    private readonly H264BInterMacroblockReconstructor _interReconstructor;
    private readonly H264IntraMacroblockReconstructor _intraReconstructor;
    private readonly H264BMacroblockHeaderReader _headerReader = new();
    private readonly H264SliceHeaderReader _sliceHeaderReader = new();
    private readonly H264IntraMacroblockSyntaxReader _intraSyntaxReader = new();
    private readonly H264IntraLumaResidualMacroblockReader _intraResidualReader = new();
    private readonly H264PcmMacroblockReader _pcmReader = new();
    private readonly H264CavlcNeighbourContext _lumaNeighbours = new();
    private readonly H264CavlcNeighbourContext _cbNeighbours = new();
    private readonly H264CavlcNeighbourContext _crNeighbours = new();
    private readonly H264BReferencePictureListBuilder _listBuilder = new();

    public H264BInterSliceDataDecoder(
        H264DecodedPictureSurface destination,
        H264MotionVectorPictureState motionVectorStateL0,
        H264MotionVectorPictureState motionVectorStateL1)
        : this(destination, motionVectorStateL0, motionVectorStateL1,
            new H264BInterMacroblockReconstructor())
    {
    }

    public H264BInterSliceDataDecoder(
        H264DecodedPictureSurface destination,
        H264MotionVectorPictureState motionVectorStateL0,
        H264MotionVectorPictureState motionVectorStateL1,
        H264BInterMacroblockReconstructor interReconstructor)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(motionVectorStateL0);
        ArgumentNullException.ThrowIfNull(motionVectorStateL1);
        ArgumentNullException.ThrowIfNull(interReconstructor);
        if (motionVectorStateL0.MacroblockWidth != destination.MacroblockWidth ||
            motionVectorStateL0.MacroblockHeight != destination.MacroblockHeight ||
            motionVectorStateL1.MacroblockWidth != destination.MacroblockWidth ||
            motionVectorStateL1.MacroblockHeight != destination.MacroblockHeight)
            throw new ArgumentException("Both motion-vector states must match the destination picture.");
        _destination = destination;
        _motionVectorStateL0 = motionVectorStateL0;
        _motionVectorStateL1 = motionVectorStateL1;
        _interReconstructor = interReconstructor;
        _intraReconstructor = new H264IntraMacroblockReconstructor(destination);
    }

    public void ResetPictureContexts()
    {
        _lumaNeighbours.Reset();
        _cbNeighbours.Reset();
        _crNeighbours.Reset();
        _intraReconstructor.ResetPictureContexts();
    }

    public H264BInterSliceDecodeResult Decode(
        H264NalUnit nalUnit,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        H264DecodedPictureBuffer decodedPictureBuffer,
        int currentPictureOrderCount,
        int sliceId,
        Func<int, int, int, int, H264TemporalDirectPredictionContext?>? collocatedContextProvider = null)
    {
        ArgumentNullException.ThrowIfNull(nalUnit);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        ArgumentNullException.ThrowIfNull(decodedPictureBuffer);
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        ValidateConfiguration(sps, pps);

        H264BitReader reader = new(nalUnit.Rbsp);
        H264SliceHeader slice = _sliceHeaderReader.Read(reader, nalUnit, sps, pps);
        H264DeblockingSliceParameters deblockingParameters =
            H264DeblockingSliceParameters.FromSliceHeader(slice);
        _intraReconstructor.SetDeblockingSliceParameters(deblockingParameters);
        if (slice.SliceType != H264SliceType.B)
            throw new NotSupportedException("This decoder accepts B slices only.");
        if (slice.FieldPicFlag || slice.ColourPlaneId != 0 || slice.RedundantPicCnt is > 0)
            throw new NotSupportedException("Field, separate-plane and redundant B slices are not supported.");

        int maxFrameNum = 1 << checked((int)sps.Log2MaxFrameNumMinus4 + 4);
        int activeCountL0 = checked((int)(slice.NumRefIdxL0ActiveMinus1 + 1));
        int activeCountL1 = checked((int)(slice.NumRefIdxL1ActiveMinus1 + 1));
        H264BReferencePictureLists referencePictures = _listBuilder.BuildLists(
            decodedPictureBuffer.Entries.Select(entry => entry.Picture),
            checked((int)slice.FrameNum), maxFrameNum, currentPictureOrderCount,
            activeCountL0, activeCountL1,
            slice.RefPicList0Modifications, slice.RefPicList1Modifications);
        if (collocatedContextProvider is null)
        {
            H264TemporalDirectMotionField? collocatedField = null;
            collocatedContextProvider = (x, y, width, height) =>
            {
                if (referencePictures.List1.Count == 0)
                    throw new FormatException("Direct prediction requires RefPicList1[0] as the collocated picture.");
                collocatedField ??= decodedPictureBuffer.CreateTemporalDirectMotionField(
                    referencePictures.List1[0]);
                return collocatedField.GetContext(
                    x, y, width, height, currentPictureOrderCount,
                    referencePictures.List0);
            };
        }
        IReadOnlyList<H264DecodedPictureSurface> referenceList0 =
            MapSurfaces(referencePictures.List0, decodedPictureBuffer);
        IReadOnlyList<H264DecodedPictureSurface> referenceList1 =
            MapSurfaces(referencePictures.List1, decodedPictureBuffer);

        uint pictureWidth = checked(sps.PicWidthInMbsMinus1 + 1);
        uint pictureHeight = checked(sps.PicHeightInMapUnitsMinus1 + 1);
        uint pictureMacroblockCount = checked(pictureWidth * pictureHeight);
        if (slice.FirstMacroblockInSlice >= pictureMacroblockCount)
            throw new FormatException("first_mb_in_slice is outside the decoded picture.");
        int qpY = checked(26 + pps.PicInitQpMinus26 + slice.SliceQpDelta);
        if (qpY is < 0 or > 51)
            throw new FormatException("The initial SliceQPY is outside the supported 8-bit range.");

        List<H264BInterMacroblockReconstructionResult> interResults = [];
        List<uint> intraAddresses = [];
        int decodedCount = 0;
        uint address = slice.FirstMacroblockInSlice;
        while (reader.MoreRbspData())
        {
            if (address >= pictureMacroblockCount)
                throw new FormatException("B-slice data contains more macroblocks than remain in the picture.");
            H264MacroblockHeader header = _headerReader.ReadNext(
                reader, address, pictureMacroblockCount, pps.EntropyCodingModeFlag);

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
                    interResults.Add(_interReconstructor.DecodeSkippedAndReconstruct(
                        oneSkipped, referenceList0, referenceList1, referencePictures,
                        _motionVectorStateL0, _motionVectorStateL1,
                        _destination, x, y, sliceId, qpY,
                        currentPictureOrderCount, slice.DirectSpatialMvPredFlag, sps, pps,
                        slice.PredictionWeights, collocatedContextProvider, deblockingParameters));
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
                _motionVectorStateL0.RecordUnavailableRegion(macroblockX * 4, macroblockY * 4, 4, 4, sliceId);
                _motionVectorStateL1.RecordUnavailableRegion(macroblockX * 4, macroblockY * 4, 4, 4, sliceId);
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
                _motionVectorStateL0.RecordUnavailableRegion(macroblockX * 4, macroblockY * 4, 4, 4, sliceId);
                _motionVectorStateL1.RecordUnavailableRegion(macroblockX * 4, macroblockY * 4, 4, 4, sliceId);
                intraAddresses.Add(address);
            }
            else
            {
                H264BInterMacroblockReconstructionResult result =
                    _interReconstructor.DecodeAndReconstruct(
                        reader, header,
                        slice.NumRefIdxL0ActiveMinus1,
                        slice.NumRefIdxL1ActiveMinus1,
                        referenceList0, referenceList1, referencePictures,
                        _motionVectorStateL0, _motionVectorStateL1,
                        _destination, macroblockX, macroblockY, sliceId,
                        qpY, currentPictureOrderCount,
                        slice.DirectSpatialMvPredFlag,
                        sps, pps, _lumaNeighbours, _cbNeighbours, _crNeighbours,
                        slice.PredictionWeights, collocatedContextProvider, deblockingParameters);
                qpY = result.QpY;
                interResults.Add(result);
            }

            decodedCount++;
            address++;
        }

        if (decodedCount == 0)
            throw new FormatException("The B slice contains no macroblock data.");
        reader.ConsumeRbspTrailingBits();
        return new H264BInterSliceDecodeResult(
            slice, decodedCount, qpY, reader.PositionBits,
            interResults, intraAddresses)
        {
            ReferencePictureLists = referencePictures
        };
    }

    private static IReadOnlyList<H264DecodedPictureSurface> MapSurfaces(
        IReadOnlyList<H264ReferencePicture> pictures,
        H264DecodedPictureBuffer dpb)
    {
        H264DecodedPictureBufferEntry[] entries = dpb.Entries.ToArray();
        return pictures.Select(picture =>
            entries.Single(entry => entry.Picture == picture).Surface).ToArray();
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

    private void ValidateConfiguration(H264SequenceParameterSet sps, H264PictureParameterSetHeader pps)
    {
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (pps.EntropyCodingModeFlag)
            throw new NotSupportedException("CABAC B-slice decoding is not implemented.");
        if (pps.NumSliceGroupsMinus1 != 0)
            throw new NotSupportedException("FMO B slices are not supported.");
        if (!sps.FrameMbsOnlyFlag || sps.MbAdaptiveFrameFieldFlag ||
            sps.SeparateColourPlaneFlag || sps.ChromaFormatIdc is not (1 or 2) ||
            sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0)
            throw new NotSupportedException("Only frame-coded 8-bit non-separate 4:2:0/4:2:2 B slices are supported.");
        if (sps.PicWidthInMbsMinus1 + 1 != (uint)_destination.MacroblockWidth ||
            sps.PicHeightInMapUnitsMinus1 + 1 != (uint)_destination.MacroblockHeight ||
            sps.ChromaFormatIdc != _destination.ChromaFormatIdc)
            throw new ArgumentException("The SPS does not describe the destination picture surface.", nameof(sps));
    }

    private static int UpdateQpY(int previousQpY, int delta)
    {
        if (delta is < -26 or > 25)
            throw new FormatException("mb_qp_delta is outside the supported 8-bit range.");
        return (previousQpY + delta + 52) % 52;
    }
}
