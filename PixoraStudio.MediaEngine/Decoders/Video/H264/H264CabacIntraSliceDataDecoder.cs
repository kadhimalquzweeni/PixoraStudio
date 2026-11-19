using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Decodes CABAC-coded I-slice macroblocks into a decoded picture surface.
/// Supports frame-only 8-bit non-separate 4:2:0 and 4:2:2 pictures with one
/// slice group. P/B slices, field coding and FMO remain separate work.
/// </summary>
public sealed class H264CabacIntraSliceDataDecoder
{
    private readonly H264DecodedPictureSurface _surface;
    private readonly H264SliceHeaderReader _sliceHeaderReader = new();
    private readonly H264CabacMacroblockHeaderReader _macroblockHeaderReader = new();
    private readonly H264CabacIntraMacroblockSyntaxReader _syntaxReader = new();
    private readonly H264CabacIntraResidualMacroblockReader _residualReader = new();
    private readonly H264PcmMacroblockReader _pcmReader = new();
    private readonly H264IntraMacroblockReconstructor _reconstructor;
    private readonly H264CabacIntraMacroblockNeighbourContext _macroblockNeighbours = new();
    private readonly H264CabacResidualNeighbourContext _residualNeighbours = new();
    private readonly Func<H264BitReader, int, bool, int, IH264CabacSliceDataReader> _cabacReaderFactory;

    public H264CabacIntraSliceDataDecoder(H264DecodedPictureSurface surface)
        : this(surface, static (reader, qpY, isIntra, cabacInitIdc) =>
            new H264CabacSliceDataReader(reader, qpY, isIntra, cabacInitIdc))
    {
    }

    internal H264CabacIntraSliceDataDecoder(
        H264DecodedPictureSurface surface,
        Func<H264BitReader, int, bool, int, IH264CabacSliceDataReader> cabacReaderFactory)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(cabacReaderFactory);
        _surface = surface;
        _reconstructor = new H264IntraMacroblockReconstructor(surface);
        _cabacReaderFactory = cabacReaderFactory;
    }

    /// <summary>
    /// Clears picture-relative context. Call once before a new picture, not
    /// between slices belonging to the same picture.
    /// </summary>
    public void ResetPictureContexts()
    {
        _reconstructor.ResetPictureContexts();
        _macroblockNeighbours.Reset();
        _residualNeighbours.Reset();
    }

    /// <summary>Decodes one complete CABAC I-slice NAL unit.</summary>
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

        H264BitReader bitReader = new(nalUnit.Rbsp);
        H264SliceHeader slice = _sliceHeaderReader.Read(
            bitReader, nalUnit, sps, pps);
        if (slice.SliceType != H264SliceType.I)
            throw new NotSupportedException("The CABAC intra slice decoder accepts I slices only.");
        if (slice.FieldPicFlag)
            throw new NotSupportedException("Field-coded slices are not supported.");
        if (slice.ColourPlaneId != 0)
            throw new NotSupportedException("Separate-colour-plane slices are not supported.");

        uint pictureWidth = checked(sps.PicWidthInMbsMinus1 + 1);
        uint pictureHeight = checked(sps.PicHeightInMapUnitsMinus1 + 1);
        uint pictureMacroblockCount = checked(pictureWidth * pictureHeight);
        if (slice.FirstMacroblockInSlice >= pictureMacroblockCount)
            throw new FormatException("first_mb_in_slice is outside the decoded picture.");
        if (pictureWidth != (uint)_surface.MacroblockWidth ||
            pictureHeight != (uint)_surface.MacroblockHeight ||
            sps.ChromaFormatIdc != _surface.ChromaFormatIdc)
            throw new ArgumentException(
                "The SPS does not describe the supplied decoded picture surface.",
                nameof(sps));

        int qpY = checked(26 + pps.PicInitQpMinus26 + slice.SliceQpDelta);
        if (qpY is < 0 or > 51)
            throw new FormatException("The initial SliceQPY is outside the supported 8-bit range.");

        _reconstructor.SetDeblockingSliceParameters(
            H264DeblockingSliceParameters.FromSliceHeader(slice));

        IH264CabacSliceDataReader cabac = _cabacReaderFactory(
            bitReader, qpY, true, 0);
        H264CabacSyntaxElementReader syntax = new(cabac);
        uint macroblockAddress = slice.FirstMacroblockInSlice;
        int decodedCount = 0;

        while (!cabac.IsTerminated)
        {
            if (macroblockAddress >= pictureMacroblockCount)
                throw new FormatException(
                    "CABAC slice data continued beyond the final picture macroblock without end_of_slice_flag.");

            int macroblockX = checked((int)(macroblockAddress % pictureWidth));
            int macroblockY = checked((int)(macroblockAddress / pictureWidth));
            int mbTypeContextIncrement =
                _macroblockNeighbours.GetMacroblockTypeContextIncrement(
                    macroblockX, macroblockY, sliceId);
            H264MacroblockHeader header = _macroblockHeaderReader.ReadIntraSlice(
                syntax, macroblockAddress, mbTypeContextIncrement);

            if (header.IsPcm)
            {
                H264PcmMacroblock pcm = _pcmReader.Read(
                    bitReader, header, sps, pps);
                _reconstructor.ReconstructPcm(
                    header, pcm, macroblockX, macroblockY,
                    sliceId, sps, pps, qpY);
                _residualReader.RecordPcmMacroblock(
                    macroblockX, macroblockY, sliceId, sps, _residualNeighbours);
                _macroblockNeighbours.RecordMacroblock(
                    macroblockAddress, macroblockX, macroblockY, sliceId,
                    isIntraNxN: false,
                    isIntra16x16: false,
                    isPcm: true,
                    codedBlockPatternLuma: 0,
                    codedBlockPatternChroma: 0,
                    intraChromaPredictionMode: null,
                    macroblockQpDelta: null);
                cabac.RestartAfterPcm();
            }
            else
            {
                uint? previousMacroblockAddress =
                    macroblockAddress == slice.FirstMacroblockInSlice
                        ? null
                        : macroblockAddress - 1;

                H264IntraMacroblockSyntax macroblockSyntax = _syntaxReader.Read(
                    syntax,
                    header,
                    sps,
                    pps,
                    _macroblockNeighbours,
                    macroblockX,
                    macroblockY,
                    sliceId,
                    previousMacroblockAddress);

                if (macroblockSyntax.MacroblockQpDelta is int delta)
                    qpY = UpdateQpY(qpY, delta);

                H264IntraLumaResidualMacroblock residual = _residualReader.Read(
                    cabac,
                    header,
                    macroblockSyntax,
                    sps,
                    macroblockX,
                    macroblockY,
                    sliceId,
                    _residualNeighbours);

                if (header.IsIntra16x16)
                {
                    _reconstructor.ReconstructIntra16x16(
                        macroblockX, macroblockY, sliceId,
                        header, macroblockSyntax, residual, qpY, sps, pps);
                }
                else if (macroblockSyntax.TransformSize8X8Flag == true)
                {
                    _reconstructor.ReconstructIntraNxN8x8(
                        macroblockX, macroblockY, sliceId,
                        header, macroblockSyntax, residual, qpY, sps, pps);
                }
                else
                {
                    _reconstructor.ReconstructIntraNxN4x4(
                        macroblockX, macroblockY, sliceId,
                        header, macroblockSyntax, residual, qpY, sps, pps);
                }

                _macroblockNeighbours.RecordMacroblock(
                    macroblockAddress,
                    macroblockX,
                    macroblockY,
                    sliceId,
                    isIntraNxN: !header.IsIntra16x16,
                    isIntra16x16: header.IsIntra16x16,
                    isPcm: false,
                    codedBlockPatternLuma: macroblockSyntax.CodedBlockPattern.Luma,
                    codedBlockPatternChroma: macroblockSyntax.CodedBlockPattern.Chroma,
                    intraChromaPredictionMode: macroblockSyntax.IntraChromaPredictionMode,
                    macroblockQpDelta: macroblockSyntax.MacroblockQpDelta,
                    transformSize8x8Flag: macroblockSyntax.TransformSize8X8Flag == true);
            }

            decodedCount++;
            if (syntax.ReadEndOfSliceFlag())
                break;

            macroblockAddress++;
        }

        if (decodedCount == 0)
            throw new FormatException("The CABAC I-slice contains no macroblock data.");

        // CABAC's arithmetic decoder reads ahead across the RBSP stop bit; its
        // bit-reader cursor is not a syntax boundary for separate trailing-bit parsing.
        return new H264IntraSliceDecodeResult(
            slice, decodedCount, qpY, bitReader.PositionBits);
    }

    private void ValidateConfiguration(
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (!pps.EntropyCodingModeFlag)
            throw new ArgumentException("The supplied PPS must enable CABAC.", nameof(pps));
        if (pps.NumSliceGroupsMinus1 != 0)
            throw new NotSupportedException("FMO slice groups are not supported.");
        if (!sps.FrameMbsOnlyFlag || sps.MbAdaptiveFrameFieldFlag)
            throw new NotSupportedException("Only frame-coded pictures are supported.");
        if (sps.SeparateColourPlaneFlag || sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Only non-separate 4:2:0 and 4:2:2 pictures are supported.");
        if (sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0)
            throw new NotSupportedException("Only 8-bit samples are supported.");
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
