using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Connects parsed I_16x16 syntax and CAVLC residual payloads to prediction,
/// inverse quantisation/transforms and the decoded picture surface. I_NxN,
/// PCM, inter, field, CABAC, 4:4:4 and high-bit-depth macroblocks are rejected.
/// </summary>
public sealed class H264IntraMacroblockReconstructor
{
    private readonly H264IntraReferenceSampleProvider _references;
    private readonly H264DecodedPictureSurface _surface;

    public H264IntraMacroblockReconstructor(H264DecodedPictureSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
        _references = new H264IntraReferenceSampleProvider(surface);
    }

    /// <summary>
    /// Reconstructs and commits one I_16x16 macroblock. qpY is SliceQPY plus
    /// the accumulated macroblock QP deltas, before the bit-depth offset.
    /// Macroblocks must be submitted in decoding order.
    /// </summary>
    public void ReconstructIntra16x16(
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264MacroblockHeader header,
        H264IntraMacroblockSyntax syntax,
        H264IntraLumaResidualMacroblock residual,
        int qpY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(residual);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        ValidateContext(macroblockX, macroblockY, sliceId, header, residual, sps, pps);

        if (!header.IsIntra16x16 || !header.IsIntra || header.IsPcm || header.IsSkipped)
            throw new NotSupportedException("This reconstruction path accepts only non-PCM I_16x16 macroblocks.");
        if (header.Intra16x16PredMode is null or > 3)
            throw new FormatException("I_16x16 macroblock is missing a valid Intra16x16 prediction mode.");
        if (syntax.LumaPredictionModes is not null || syntax.TransformSize8X8Flag == true)
            throw new FormatException("I_16x16 macroblocks cannot carry I_NxN luma prediction modes.");
        if (syntax.IntraChromaPredictionMode is null or > 3)
            throw new FormatException("The chroma prediction mode must be present and in the range 0..3.");
        if (syntax.CodedBlockPattern.ChromaArrayType != sps.ChromaFormatIdc ||
            syntax.CodedBlockPattern.Luma > 15 ||
            syntax.CodedBlockPattern.Chroma > 2)
            throw new FormatException("The intra macroblock coded-block-pattern does not match the active chroma format.");
        if (header.CodedBlockPatternLuma != syntax.CodedBlockPattern.Luma ||
            header.CodedBlockPatternChroma != syntax.CodedBlockPattern.Chroma)
            throw new FormatException("I_16x16 macroblock header and parsed coded-block-pattern values disagree.");
        if (residual.ChromaComponents.Count != 2 ||
            residual.ChromaComponents.Select(component => component.ComponentId).Distinct().Count() != 2 ||
            !residual.ChromaComponents.Any(component => component.ComponentId == 1) ||
            !residual.ChromaComponents.Any(component => component.ComponentId == 2))
            throw new FormatException("I_16x16 residual data must contain exactly one Cb and one Cr component.");
        H264CavlcResidualBlock dcResidual = residual.Intra16x16Dc
            ?? throw new FormatException("I_16x16 residual data must contain a DC block.");
        if (residual.Blocks.Count != 16)
            throw new FormatException("I_16x16 residual data must contain sixteen AC blocks.");

        H264IntraReferenceSamples lumaReferences = _references.GatherLuma16x16(
            macroblockX, macroblockY, sliceId, pps.ConstrainedIntraPredFlag);
        if (header.Intra16x16PredMode == 0 && !lumaReferences.HasTop ||
            header.Intra16x16PredMode == 1 && !lumaReferences.HasLeft ||
            header.Intra16x16PredMode == 3 &&
                (!lumaReferences.HasTop || !lumaReferences.HasLeft || !lumaReferences.HasTopLeft))
            throw new FormatException("The selected Intra16x16 luma mode requires unavailable reference samples.");

        int qpPrimeY = H264InverseQuantizer.ToQpPrimeY(sps, qpY);
        byte[] lumaPrediction = H264IntraPrediction.Predict16x16(
            header.Intra16x16PredMode.Value,
            lumaReferences.Top, lumaReferences.Left, lumaReferences.TopLeft,
            lumaReferences.HasTop, lumaReferences.HasLeft);
        H264CavlcResidualBlock[] acBlocks = OrderLumaAcBlocks(residual.Blocks);
        byte[] luma = H264Intra16x16ResidualReconstructor.Reconstruct(
            lumaPrediction, dcResidual, acBlocks,
            qpPrimeY, sps, pps);

        byte[] cb = ReconstructChroma(1, macroblockX, macroblockY, sliceId,
            syntax.IntraChromaPredictionMode.Value, residual, qpY, sps, pps);
        byte[] cr = ReconstructChroma(2, macroblockX, macroblockY, sliceId,
            syntax.IntraChromaPredictionMode.Value, residual, qpY, sps, pps);

        _surface.WriteIntraMacroblock(macroblockX, macroblockY, sliceId, luma, cb, cr);
    }

    private byte[] ReconstructChroma(
        byte componentId, int macroblockX, int macroblockY, int sliceId,
        uint mode, H264IntraLumaResidualMacroblock residual,
        int qpY, H264SequenceParameterSet sps, H264PictureParameterSetHeader pps)
    {
        H264IntraChromaResidualComponent component = residual.ChromaComponents
            .SingleOrDefault(item => item.ComponentId == componentId)
            ?? throw new FormatException($"Missing chroma residual component {componentId}.");
        H264IntraReferenceSamples references = _references.GatherChroma(
            componentId, macroblockX, macroblockY, sliceId, pps.ConstrainedIntraPredFlag);
        if (mode == 1 && !references.HasLeft ||
            mode == 2 && !references.HasTop ||
            mode == 3 && (!references.HasTop || !references.HasLeft || !references.HasTopLeft))
            throw new FormatException("The selected chroma prediction mode requires unavailable reference samples.");

        byte[] prediction = H264ChromaIntraPrediction.Predict(
            mode, sps.ChromaFormatIdc, references.Top, references.Left,
            references.TopLeft, references.HasTop, references.HasLeft,
            references.HasTopLeft);
        return H264IntraChromaResidualReconstructor.Reconstruct(
            prediction, component, qpY, sps, pps);
    }

    private void ValidateContext(
        int x, int y, int sliceId, H264MacroblockHeader header,
        H264IntraLumaResidualMacroblock residual,
        H264SequenceParameterSet sps, H264PictureParameterSetHeader pps)
    {
        if ((uint)x >= (uint)_surface.MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)_surface.MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (!sps.FrameMbsOnlyFlag || sps.SeparateColourPlaneFlag ||
            sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0 ||
            sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Only frame-only 8-bit non-separate 4:2:0 and 4:2:2 reconstruction is supported.");
        if (sps.PicWidthInMbsMinus1 + 1 != (uint)_surface.MacroblockWidth ||
            sps.PicHeightInMapUnitsMinus1 + 1 != (uint)_surface.MacroblockHeight ||
            sps.ChromaFormatIdc != _surface.ChromaFormatIdc)
            throw new ArgumentException("The SPS does not describe the supplied decoded picture surface.", nameof(sps));
        if (pps.EntropyCodingModeFlag)
            throw new NotSupportedException("CABAC macroblock reconstruction is not implemented.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        uint expectedAddress = checked((uint)(y * _surface.MacroblockWidth + x));
        if (header.MacroblockAddress != expectedAddress ||
            residual.MacroblockAddress != expectedAddress)
            throw new FormatException("Macroblock header/residual addresses do not match the requested picture coordinate.");
        if (_surface.IsMacroblockDecoded(x, y))
            throw new InvalidOperationException("The macroblock has already been reconstructed.");
        if (!header.IsIntra || header.IsSkipped || header.IsPcm)
            throw new NotSupportedException("Only coded intra macroblocks are supported by this reconstruction stage.");
    }

    private static H264CavlcResidualBlock[] OrderLumaAcBlocks(
        IReadOnlyList<H264IntraLumaResidualBlock> blocks)
    {
        if (blocks.Count != 16)
            throw new FormatException("An I_16x16 macroblock must contain sixteen luma AC blocks.");
        var ordered = new H264CavlcResidualBlock[16];
        var seen = new bool[16];
        foreach (H264IntraLumaResidualBlock block in blocks)
        {
            if (block.BlockX is < 0 or > 3 || block.BlockY is < 0 or > 3)
                throw new FormatException("Luma residual block coordinates must be within the macroblock.");
            int scanIndex = block.BlockY / 2 * 8 + block.BlockX / 2 * 4 +
                block.BlockY % 2 * 2 + block.BlockX % 2;
            if (seen[scanIndex])
                throw new FormatException("Duplicate luma residual block coordinates.");
            seen[scanIndex] = true;
            if (block.Residual.MaximumCoefficients != 15)
                throw new FormatException("I_16x16 luma AC blocks must contain fifteen coefficients.");
            ordered[scanIndex] = block.Residual;
        }
        if (seen.Any(value => !value))
            throw new FormatException("The I_16x16 luma residual block grid is incomplete.");
        return ordered;
    }
}
