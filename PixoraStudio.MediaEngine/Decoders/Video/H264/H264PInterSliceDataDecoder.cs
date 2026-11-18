using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Traverses and reconstructs CAVLC P slices containing P_L0 inter macroblocks
/// and P_Skip runs. P-slice intra macroblocks, CABAC, FMO, field coding,
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
    }

    public H264PInterSliceDecodeResult Decode(
        H264NalUnit nalUnit,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        IReadOnlyList<H264DecodedPictureSurface> activeReferenceList0,
        int sliceId)
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
                        slice.PredictionWeights));
                }
                address = checked(address + header.SkippedMacroblockCount);
                continue;
            }

            if (header.IsIntra)
                throw new NotSupportedException("Intra-coded macroblocks inside P slices are not yet supported.");

            int macroblockX = checked((int)(address % pictureWidth));
            int macroblockY = checked((int)(address / pictureWidth));
            H264InterMacroblockReconstructionResult reconstructed =
                _macroblockReconstructor.DecodeAndReconstruct(
                    reader, header, slice.NumRefIdxL0ActiveMinus1,
                    activeReferenceList0, _motionVectorState, _destination,
                    macroblockX, macroblockY, sliceId, qpY, sps, pps,
                    _lumaNeighbours, _cbNeighbours, _crNeighbours,
                    slice.PredictionWeights);
            qpY = reconstructed.QpY;
            results.Add(reconstructed);
            address++;
        }

        if (results.Count == 0)
            throw new FormatException("The P slice contains no macroblock data.");

        reader.ConsumeRbspTrailingBits();
        return new H264PInterSliceDecodeResult(
            slice, results.Count, qpY, reader.PositionBits, results);
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
