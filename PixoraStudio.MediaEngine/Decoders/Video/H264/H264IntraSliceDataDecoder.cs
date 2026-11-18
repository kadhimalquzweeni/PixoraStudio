using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Decodes CAVLC or CABAC I-slice macroblock data into an existing frame surface.
/// The supported path is frame-only, 8-bit, non-separate 4:2:0/4:2:2 with
/// one slice group. Macroblocks are decoded in raster order.
/// </summary>
public sealed class H264IntraSliceDataDecoder
{
    private readonly H264DecodedPictureSurface _surface;
    private readonly H264SliceHeaderReader _sliceHeaderReader = new();
    private readonly H264MacroblockHeaderReader _macroblockHeaderReader = new();
    private readonly H264IntraMacroblockSyntaxReader _syntaxReader = new();
    private readonly H264IntraLumaResidualMacroblockReader _residualReader = new();
    private readonly H264PcmMacroblockReader _pcmReader = new();
    private readonly H264IntraMacroblockReconstructor _reconstructor;
    private readonly H264CavlcNeighbourContext _lumaNeighbours = new();
    private readonly H264CavlcNeighbourContext _cbNeighbours = new();
    private readonly H264CavlcNeighbourContext _crNeighbours = new();
    private readonly H264CabacMacroblockHeaderReader _cabacMacroblockHeaderReader = new();
    private readonly H264CabacIntraMacroblockSyntaxReader _cabacSyntaxReader = new();
    private readonly H264CabacIntraResidualMacroblockReader _cabacResidualReader = new();
    private readonly H264CabacIntraMacroblockNeighbourContext _cabacMacroblockNeighbours = new();
    private readonly H264CabacResidualNeighbourContext _cabacResidualNeighbours = new();

    public H264IntraSliceDataDecoder(H264DecodedPictureSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
        _reconstructor = new H264IntraMacroblockReconstructor(surface);
    }

    /// <summary>
    /// Clears picture-relative intra and CAVLC neighbour state. Call once
    /// before decoding a new coded picture, not between slices of that picture.
    /// </summary>
    public void ResetPictureContexts()
    {
        _reconstructor.ResetPictureContexts();
        _lumaNeighbours.Reset();
        _cbNeighbours.Reset();
        _crNeighbours.Reset();
        _cabacMacroblockNeighbours.Reset();
        _cabacResidualNeighbours.Reset();
    }

    /// <summary>
    /// Decodes one complete I-slice NAL unit. The supplied sliceId must be
    /// unique within the picture and shared by every macroblock in this slice.
    /// </summary>
    public H264IntraSliceDecodeResult Decode(
        H264NalUnit nalUnit,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        int sliceId)
    {
        ArgumentNullException.ThrowIfNull(nalUnit);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        ValidateConfiguration(sps, pps);

        H264BitReader reader = new(nalUnit.Rbsp);
        H264SliceHeader slice = _sliceHeaderReader.Read(reader, nalUnit, sps, pps);
        _reconstructor.SetDeblockingSliceParameters(
            H264DeblockingSliceParameters.FromSliceHeader(slice));
        if (slice.SliceType != H264SliceType.I)
            throw new NotSupportedException("This decoder currently accepts I slices only.");
        if (slice.FieldPicFlag)
            throw new NotSupportedException("Field-coded slices are not supported.");
        if (slice.ColourPlaneId != 0)
            throw new NotSupportedException("Separate-colour-plane slices are not supported.");

        uint pictureWidth = checked(sps.PicWidthInMbsMinus1 + 1);
        uint pictureHeight = checked(sps.PicHeightInMapUnitsMinus1 + 1);
        uint pictureMacroblockCount = checked(pictureWidth * pictureHeight);
        if (slice.FirstMacroblockInSlice >= pictureMacroblockCount)
            throw new FormatException("first_mb_in_slice is outside the decoded picture.");

        int qpY = checked(26 + pps.PicInitQpMinus26 + slice.SliceQpDelta);
        if (qpY is < 0 or > 51)
            throw new FormatException("The initial SliceQPY is outside the supported 8-bit range.");

        if (pps.EntropyCodingModeFlag)
        {
            return DecodeCabac(reader, slice, sps, pps, sliceId, qpY);
        }

        uint address = slice.FirstMacroblockInSlice;
        int decodedCount = 0;
        while (reader.MoreRbspData())
        {
            if (address >= pictureMacroblockCount)
                throw new FormatException("Slice data contains more macroblocks than remain in the picture.");

            H264MacroblockHeader header = _macroblockHeaderReader.ReadNext(
                reader, H264SliceType.I, address, pictureMacroblockCount,
                pps.EntropyCodingModeFlag);
            int macroblockX = checked((int)(address % pictureWidth));
            int macroblockY = checked((int)(address / pictureWidth));

            if (header.IsPcm)
            {
                H264PcmMacroblock pcm = _pcmReader.Read(reader, header, sps, pps);
                _reconstructor.ReconstructPcm(
                    header, pcm, macroblockX, macroblockY, sliceId, sps, pps, qpY: qpY);
                RecordPcmNeighbourCounts(macroblockX, macroblockY, sliceId, sps);
            }
            else
            {
                H264IntraMacroblockSyntax syntax = _syntaxReader.Read(
                    reader, header, sps, pps);
                if (syntax.MacroblockQpDelta is int delta)
                    qpY = UpdateQpY(qpY, delta);

                H264IntraLumaResidualMacroblock residual = _residualReader.Read(
                    reader, header, syntax, sps, macroblockX, macroblockY, sliceId,
                    _lumaNeighbours, _cbNeighbours, _crNeighbours);

                if (header.IsIntra16x16)
                {
                    _reconstructor.ReconstructIntra16x16(
                        macroblockX, macroblockY, sliceId, header, syntax,
                        residual, qpY, sps, pps);
                }
                else if (syntax.TransformSize8X8Flag == true)
                {
                    _reconstructor.ReconstructIntraNxN8x8(
                        macroblockX, macroblockY, sliceId, header, syntax,
                        residual, qpY, sps, pps);
                }
                else
                {
                    _reconstructor.ReconstructIntraNxN4x4(
                        macroblockX, macroblockY, sliceId, header, syntax,
                        residual, qpY, sps, pps);
                }
            }

            decodedCount++;
            address++;
        }

        if (decodedCount == 0)
            throw new FormatException("The I-slice contains no macroblock data.");

        reader.ConsumeRbspTrailingBits();
        return new H264IntraSliceDecodeResult(
            slice, decodedCount, qpY, reader.PositionBits);
    }

    private void ValidateConfiguration(
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (pps.NumSliceGroupsMinus1 != 0)
            throw new NotSupportedException("FMO slice groups are not supported by this slice decoder.");
        if (!sps.FrameMbsOnlyFlag || sps.MbAdaptiveFrameFieldFlag)
            throw new NotSupportedException("Only frame-coded pictures are supported.");
        if (sps.SeparateColourPlaneFlag || sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Only non-separate 4:2:0 and 4:2:2 pictures are supported.");
        if (sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0)
            throw new NotSupportedException("Only 8-bit samples are supported.");
        if (sps.PicWidthInMbsMinus1 + 1 != (uint)_surface.MacroblockWidth ||
            sps.PicHeightInMapUnitsMinus1 + 1 != (uint)_surface.MacroblockHeight ||
            sps.ChromaFormatIdc != _surface.ChromaFormatIdc)
            throw new ArgumentException("The SPS does not describe the supplied decoded picture surface.", nameof(sps));
    }


    private H264IntraSliceDecodeResult DecodeCabac(
        H264BitReader reader,
        H264SliceHeader slice,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        int sliceId,
        int initialQpY)
    {
        H264CabacSliceDataReader cabac = new(
            reader, initialQpY, isIntraSlice: true, cabacInitIdc: 0);
        H264CabacSyntaxElementReader syntaxReader = new(cabac);
        uint pictureWidth = checked(sps.PicWidthInMbsMinus1 + 1);
        uint pictureMacroblockCount = checked(
            pictureWidth * (sps.PicHeightInMapUnitsMinus1 + 1));
        uint address = slice.FirstMacroblockInSlice;
        int qpY = initialQpY;
        int decodedCount = 0;

        while (!cabac.IsTerminated)
        {
            if (address >= pictureMacroblockCount)
                throw new FormatException(
                    "CABAC I-slice data continued beyond the final picture macroblock.");

            int macroblockX = checked((int)(address % pictureWidth));
            int macroblockY = checked((int)(address / pictureWidth));
            int macroblockTypeContextIncrement =
                _cabacMacroblockNeighbours.GetMacroblockTypeContextIncrement(
                    macroblockX, macroblockY, sliceId);
            H264MacroblockHeader header = _cabacMacroblockHeaderReader.ReadIntraSlice(
                syntaxReader, address, macroblockTypeContextIncrement);

            if (header.IsPcm)
            {
                H264PcmMacroblock pcm = _pcmReader.Read(reader, header, sps, pps);
                _reconstructor.ReconstructPcm(
                    header, pcm, macroblockX, macroblockY, sliceId, sps, pps, qpY);
                _cabacResidualReader.RecordPcmMacroblock(
                    macroblockX, macroblockY, sliceId, sps, _cabacResidualNeighbours);
                _cabacMacroblockNeighbours.RecordMacroblock(
                    address, macroblockX, macroblockY, sliceId,
                    isIntraNxN: false, isIntra16x16: false, isPcm: true,
                    codedBlockPatternLuma: 0, codedBlockPatternChroma: 0,
                    intraChromaPredictionMode: null, macroblockQpDelta: null,
                    transformSize8x8Flag: false);
                cabac.RestartAfterPcm();
            }
            else
            {
                uint? previousAddress = address == slice.FirstMacroblockInSlice
                    ? null
                    : address - 1;
                H264IntraMacroblockSyntax syntax = _cabacSyntaxReader.Read(
                    syntaxReader, header, sps, pps,
                    _cabacMacroblockNeighbours,
                    macroblockX, macroblockY, sliceId, previousAddress);
                H264IntraLumaResidualMacroblock residual = _cabacResidualReader.Read(
                    cabac, header, syntax, sps,
                    macroblockX, macroblockY, sliceId, _cabacResidualNeighbours);

                if (syntax.MacroblockQpDelta is int delta)
                    qpY = UpdateQpY(qpY, delta);

                if (header.IsIntra16x16)
                {
                    _reconstructor.ReconstructIntra16x16(
                        macroblockX, macroblockY, sliceId, header, syntax,
                        residual, qpY, sps, pps);
                }
                else if (syntax.TransformSize8X8Flag == true)
                {
                    _reconstructor.ReconstructIntraNxN8x8(
                        macroblockX, macroblockY, sliceId, header, syntax,
                        residual, qpY, sps, pps);
                }
                else
                {
                    _reconstructor.ReconstructIntraNxN4x4(
                        macroblockX, macroblockY, sliceId, header, syntax,
                        residual, qpY, sps, pps);
                }

                _cabacMacroblockNeighbours.RecordMacroblock(
                    address, macroblockX, macroblockY, sliceId,
                    isIntraNxN: !header.IsIntra16x16,
                    isIntra16x16: header.IsIntra16x16,
                    isPcm: false,
                    codedBlockPatternLuma: syntax.CodedBlockPattern.Luma,
                    codedBlockPatternChroma: syntax.CodedBlockPattern.Chroma,
                    intraChromaPredictionMode: syntax.IntraChromaPredictionMode,
                    macroblockQpDelta: syntax.MacroblockQpDelta,
                    transformSize8x8Flag: syntax.TransformSize8X8Flag == true);
            }

            decodedCount++;
            bool endOfSlice = syntaxReader.ReadEndOfSliceFlag();
            address++;
            if (endOfSlice)
                break;
        }

        if (decodedCount == 0)
            throw new FormatException("The CABAC I slice contains no macroblock data.");

        reader.ConsumeCabacTrailingBits();
        return new H264IntraSliceDecodeResult(
            slice, decodedCount, qpY, reader.PositionBits);
    }

    private void RecordPcmNeighbourCounts(int macroblockX, int macroblockY, int sliceId,
        H264SequenceParameterSet sps)
    {
        int lumaX = macroblockX * 4;
        int lumaY = macroblockY * 4;
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            _lumaNeighbours.RecordBlock(lumaX + x, lumaY + y, sliceId, 16);

        int chromaBlocksHigh = sps.ChromaFormatIdc == 1 ? 2 : 4;
        int chromaX = macroblockX * 2;
        int chromaY = macroblockY * chromaBlocksHigh;
        for (int y = 0; y < chromaBlocksHigh; y++)
        for (int x = 0; x < 2; x++)
        {
            _cbNeighbours.RecordBlock(chromaX + x, chromaY + y, sliceId, 16);
            _crNeighbours.RecordBlock(chromaX + x, chromaY + y, sliceId, 16);
        }
    }

    private static int UpdateQpY(int previousQpY, int macroblockQpDelta)
    {
        if (previousQpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(previousQpY));
        if (macroblockQpDelta is < -26 or > 25)
            throw new FormatException("mb_qp_delta is outside the supported 8-bit range.");
        return (previousQpY + macroblockQpDelta + 52) % 52;
    }
}
