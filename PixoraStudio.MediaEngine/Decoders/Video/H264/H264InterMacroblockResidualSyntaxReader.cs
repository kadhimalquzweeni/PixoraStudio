using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parses coded_block_pattern and mb_qp_delta for a CAVLC inter macroblock.
/// Inter prediction syntax must already have been consumed. This stage is
/// limited to frame-coded 8-bit non-separate 4:2:0/4:2:2 pictures.
/// </summary>
public sealed class H264InterMacroblockResidualSyntaxReader
{
    private readonly H264CodedBlockPatternReader _codedBlockPatternReader = new();

    public H264InterMacroblockResidualSyntax Read(
        H264BitReader reader,
        H264MacroblockHeader header,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        bool noSubMbPartSizeLessThan8x8Flag = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (header.IsIntra || header.IsSkipped || header.IsPcm)
            throw new ArgumentException("The macroblock header must describe a coded inter macroblock.", nameof(header));
        if (pps.EntropyCodingModeFlag)
            throw new NotSupportedException("CABAC inter macroblock syntax is not implemented.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (!sps.FrameMbsOnlyFlag || sps.SeparateColourPlaneFlag ||
            sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0 ||
            sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Inter residual syntax supports frame-only 8-bit non-separate 4:2:0 and 4:2:2 only.");

        H264CodedBlockPattern pattern =
            _codedBlockPatternReader.ReadInter(reader, sps.ChromaFormatIdc);

        // For inter macroblocks this optional flag follows coded_block_pattern
        // and precedes mb_qp_delta. It is present only when every subpartition
        // is at least 8x8 and the PPS enables 8x8 transforms.
        bool transformSize8X8Flag = pattern.Luma > 0 && pps.Transform8X8ModeFlag &&
            noSubMbPartSizeLessThan8x8Flag && reader.ReadFlag();

        int? qpDelta = pattern.Luma != 0 || pattern.Chroma != 0
            ? reader.ReadSignedExpGolomb()
            : null;

        return new H264InterMacroblockResidualSyntax(pattern, qpDelta)
        {
            TransformSize8X8Flag = transformSize8X8Flag
        };
    }
}
