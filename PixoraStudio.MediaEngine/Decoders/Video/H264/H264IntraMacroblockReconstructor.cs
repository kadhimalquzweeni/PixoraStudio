using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Connects supported intra macroblock syntax and decoded residual payloads
/// to prediction, inverse quantisation/transforms and the decoded picture
/// surface. I_16x16, Intra4x4, Intra8x8 and I_PCM are supported for the
/// implemented frame-only 8-bit 4:2:0/4:2:2 paths; inter, field,
/// 4:4:4 and high-bit-depth macroblocks remain unsupported.
/// </summary>
public sealed class H264IntraMacroblockReconstructor
{
    private readonly H264IntraReferenceSampleProvider _references;
    private readonly H264DecodedPictureSurface _surface;
    private readonly H264Intra4x4PredictionContext _intra4x4Modes = new();
    private readonly H264Intra8x8PredictionContext _intra8x8Modes = new();
    private readonly H264Intra4x4MacroblockLumaReconstructor _intra4x4Luma = new();
    private readonly H264Intra8x8MacroblockLumaReconstructor _intra8x8Luma = new();
    private H264DeblockingSliceParameters _deblockingParameters = new(0, 0, 0);

    public void SetDeblockingSliceParameters(H264DeblockingSliceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _deblockingParameters = parameters;
    }

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
            throw new FormatException(
                $"Intra16x16 prediction mode {header.Intra16x16PredMode} at macroblock " +
                $"({macroblockX}, {macroblockY}) requires unavailable reference samples " +
                $"(top: {lumaReferences.HasTop}, left: {lumaReferences.HasLeft}, " +
                $"top-left: {lumaReferences.HasTopLeft}, slice: {sliceId}).");

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
        RecordIntra16x16Modes(macroblockX, macroblockY, sliceId);
        AttachIntraDeblockingMetadata(macroblockX, macroblockY, qpY, false, sps, pps);
    }

    /// <summary>
    /// Reconstructs and commits one I_NxN macroblock using sixteen Intra4x4
    /// luma blocks. Use ReconstructIntraNxN8x8 when transform_size_8x8_flag is set.
    /// </summary>
    public void ReconstructIntraNxN4x4(
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

        if (header.IsIntra16x16)
            throw new NotSupportedException("The I_NxN path cannot reconstruct I_16x16 macroblocks.");
        if (pps.Transform8X8ModeFlag != syntax.TransformSize8X8Flag.HasValue)
            throw new FormatException("transform_size_8x8_flag presence does not match the PPS.");
        if (syntax.TransformSize8X8Flag == true)
            throw new NotSupportedException("Use ReconstructIntraNxN8x8 for Intra8x8 macroblocks.");
        H264IntraPredictionModeSyntax modes = syntax.LumaPredictionModes
            ?? throw new FormatException("I_NxN macroblock is missing luma prediction modes.");
        if (modes.Uses8X8Transform)
            throw new NotSupportedException("This path requires sixteen Intra4x4 prediction modes.");
        if (modes.Modes.Count != 16)
            throw new FormatException("An Intra4x4 macroblock must contain sixteen prediction-mode entries.");
        if (syntax.IntraChromaPredictionMode is null or > 3)
            throw new FormatException("The chroma prediction mode must be present and in the range 0..3.");
        if (syntax.CodedBlockPattern.ChromaArrayType != sps.ChromaFormatIdc ||
            syntax.CodedBlockPattern.Luma > 15 ||
            syntax.CodedBlockPattern.Chroma > 2)
            throw new FormatException("The intra macroblock coded-block-pattern does not match the active chroma format.");
        if (residual.Intra16x16Dc is not null)
            throw new FormatException("I_NxN macroblocks cannot contain an Intra16x16 luma DC block.");
        if (residual.ChromaComponents.Count != 2 ||
            residual.ChromaComponents.Select(component => component.ComponentId).Distinct().Count() != 2 ||
            !residual.ChromaComponents.Any(component => component.ComponentId == 1) ||
            !residual.ChromaComponents.Any(component => component.ComponentId == 2))
            throw new FormatException("I_NxN residual data must contain exactly one Cb and one Cr component.");

        H264IntraReferenceSamples lumaReferences = _references.GatherLuma16x16(
            macroblockX, macroblockY, sliceId, pps.ConstrainedIntraPredFlag);
        byte[] luma = _intra4x4Luma.Reconstruct(
            macroblockX, macroblockY, sliceId,
            lumaReferences.Top, lumaReferences.Left, lumaReferences.TopLeft,
            lumaReferences.HasTop, lumaReferences.HasLeft, lumaReferences.HasTopLeft,
            modes, residual.Blocks, _intra4x4Modes, qpY, sps, pps);

        byte[] cb = ReconstructChroma(1, macroblockX, macroblockY, sliceId,
            syntax.IntraChromaPredictionMode.Value, residual, qpY, sps, pps);
        byte[] cr = ReconstructChroma(2, macroblockX, macroblockY, sliceId,
            syntax.IntraChromaPredictionMode.Value, residual, qpY, sps, pps);

        _surface.WriteIntraMacroblock(macroblockX, macroblockY, sliceId, luma, cb, cr);
        RecordIntra4x4MacroblockModesAs8x8(macroblockX, macroblockY, sliceId);
        AttachIntraDeblockingMetadata(macroblockX, macroblockY, qpY, false, sps, pps);
    }

    /// <summary>
    /// Reconstructs and commits one I_NxN macroblock using four Intra8x8
    /// luma transform blocks and the shared chroma reconstruction path.
    /// </summary>
    public void ReconstructIntraNxN8x8(
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

        if (header.IsIntra16x16)
            throw new NotSupportedException("The I_NxN path cannot reconstruct I_16x16 macroblocks.");
        if (!pps.Transform8X8ModeFlag || syntax.TransformSize8X8Flag != true)
            throw new FormatException("Intra8x8 reconstruction requires transform_size_8x8_flag to be present and true.");
        H264IntraPredictionModeSyntax modes = syntax.LumaPredictionModes
            ?? throw new FormatException("I_NxN macroblock is missing luma prediction modes.");
        if (!modes.Uses8X8Transform || modes.Modes.Count != 4)
            throw new FormatException("An Intra8x8 macroblock must contain four prediction modes.");
        if (syntax.IntraChromaPredictionMode is null or > 3)
            throw new FormatException("The chroma prediction mode must be present and in the range 0..3.");
        if (syntax.CodedBlockPattern.ChromaArrayType != sps.ChromaFormatIdc ||
            syntax.CodedBlockPattern.Luma > 15 || syntax.CodedBlockPattern.Chroma > 2)
            throw new FormatException("The coded-block-pattern does not match the active chroma format.");
        if (residual.Intra16x16Dc is not null || residual.Blocks.Count != 0 ||
            residual.Intra8x8Blocks.Count != 4)
            throw new FormatException("Intra8x8 residual data must contain exactly four 8x8 blocks and no 4x4/DC blocks.");
        foreach (H264Intra8x8LumaResidualBlock block in residual.Intra8x8Blocks)
        {
            if (block.BlockX is < 0 or > 1 || block.BlockY is < 0 or > 1)
                throw new FormatException("Intra8x8 residual coordinates must be within the macroblock.");
            int blockIndex = block.BlockY * 2 + block.BlockX;
            bool expectedCoded = (syntax.CodedBlockPattern.Luma & (1u << blockIndex)) != 0;
            if (block.IsCoded != expectedCoded)
                throw new FormatException("Intra8x8 residual coded flags do not match coded_block_pattern_luma.");
        }
        if (residual.ChromaComponents.Count != 2 ||
            residual.ChromaComponents.Select(component => component.ComponentId).Distinct().Count() != 2 ||
            !residual.ChromaComponents.Any(component => component.ComponentId == 1) ||
            !residual.ChromaComponents.Any(component => component.ComponentId == 2))
            throw new FormatException("Intra8x8 residual data must contain exactly one Cb and one Cr component.");

        H264IntraReferenceSamples references = _references.GatherLuma16x16(
            macroblockX, macroblockY, sliceId, pps.ConstrainedIntraPredFlag);
        (byte[] topRight, bool hasTopRight) = _references.GatherLumaTopRight8(
            macroblockX, macroblockY, sliceId, pps.ConstrainedIntraPredFlag);
        byte[] luma = _intra8x8Luma.Reconstruct(
            macroblockX, macroblockY, sliceId,
            references.Top, references.Left, references.TopLeft,
            references.HasTop, references.HasLeft, references.HasTopLeft,
            topRight, hasTopRight,
            modes, residual.Intra8x8Blocks, _intra8x8Modes, _intra4x4Modes,
            qpY, sps, pps);

        byte[] cb = ReconstructChroma(1, macroblockX, macroblockY, sliceId,
            syntax.IntraChromaPredictionMode.Value, residual, qpY, sps, pps);
        byte[] cr = ReconstructChroma(2, macroblockX, macroblockY, sliceId,
            syntax.IntraChromaPredictionMode.Value, residual, qpY, sps, pps);
        _surface.WriteIntraMacroblock(macroblockX, macroblockY, sliceId, luma, cb, cr);
        AttachIntraDeblockingMetadata(macroblockX, macroblockY, qpY, true, sps, pps);
    }

    /// <summary>
    /// Commits an I_PCM macroblock. PCM samples bypass prediction and
    /// transform processing but are still marked as intra for constrained
    /// intra prediction by subsequent macroblocks.
    /// </summary>
    public void ReconstructPcm(
        H264MacroblockHeader header,
        H264PcmMacroblock pcm,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        int? qpY = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(pcm);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (!header.IsPcm || !header.IsIntra || header.IsSkipped)
            throw new ArgumentException("The macroblock header must describe an I_PCM macroblock.", nameof(header));
        if (macroblockX < 0 || macroblockX >= _surface.MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (macroblockY < 0 || macroblockY >= _surface.MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (!sps.FrameMbsOnlyFlag || sps.SeparateColourPlaneFlag ||
            sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0 ||
            sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("I_PCM reconstruction supports frame-only 8-bit non-separate 4:2:0 and 4:2:2 pictures.");
        if (pps.SequenceParameterSetId != sps.Id ||
            sps.PicWidthInMbsMinus1 + 1 != (uint)_surface.MacroblockWidth ||
            sps.PicHeightInMapUnitsMinus1 + 1 != (uint)_surface.MacroblockHeight ||
            sps.ChromaFormatIdc != _surface.ChromaFormatIdc)
            throw new ArgumentException("The SPS/PPS do not describe the supplied decoded picture surface.");
        uint address = checked((uint)(macroblockY * _surface.MacroblockWidth + macroblockX));
        if (header.MacroblockAddress != address || pcm.MacroblockAddress != address)
            throw new FormatException("I_PCM macroblock addresses do not match the requested picture coordinate.");
        if (_surface.IsMacroblockDecoded(macroblockX, macroblockY))
            throw new InvalidOperationException("The macroblock has already been reconstructed.");

        int expectedChromaSamples = 8 * (sps.ChromaFormatIdc == 1 ? 8 : 16);
        if (pcm.LumaSamples.Count != 256 ||
            pcm.CbSamples.Count != expectedChromaSamples ||
            pcm.CrSamples.Count != expectedChromaSamples)
            throw new FormatException("I_PCM sample counts do not match the active chroma format.");

        _surface.WriteMacroblock(macroblockX, macroblockY, sliceId, true,
            pcm.LumaSamples, pcm.CbSamples, pcm.CrSamples);
        RecordIntra16x16Modes(macroblockX, macroblockY, sliceId);
        if (qpY.HasValue)
            // H.264 specifies QP_y = 0 for I_PCM macroblocks during deblocking.
            AttachIntraDeblockingMetadata(macroblockX, macroblockY, 0, false, sps, pps);
    }

    /// <summary>Clears neighbour-derived mode state at a new picture boundary.</summary>
    public void ResetPictureContexts()
    {
        _intra4x4Modes.Reset();
        _intra8x8Modes.Reset();
    }

    private void AttachIntraDeblockingMetadata(
        int macroblockX,
        int macroblockY,
        int qpY,
        bool transformSize8x8Flag,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        int qpCb = H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(
            qpY, 1, sps, pps);
        int qpCr = H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(
            qpY, 2, sps, pps);
        H264DeblockingBlockMetadata intraBlock =
            new(true, false, null, null, null, null);
        int chromaBlockCount = sps.ChromaFormatIdc == 1 ? 4 : 8;
        _surface.SetDeblockingMetadata(macroblockX, macroblockY,
            new H264DeblockingMacroblockMetadata(
                qpY, qpCb, qpCr,
                _deblockingParameters.DisableDeblockingFilterIdc,
                _deblockingParameters.AlphaOffsetDiv2,
                _deblockingParameters.BetaOffsetDiv2,
                transformSize8x8Flag,
                Enumerable.Repeat(intraBlock, 16).ToArray(),
                Enumerable.Repeat(intraBlock, chromaBlockCount).ToArray(),
                Enumerable.Repeat(intraBlock, chromaBlockCount).ToArray()));
    }

    private void RecordIntra16x16Modes(int macroblockX, int macroblockY, int sliceId)
    {
        for (int blockY = 0; blockY < 4; blockY++)
        for (int blockX = 0; blockX < 4; blockX++)
            _intra4x4Modes.RecordMode(
                macroblockX * 4 + blockX, macroblockY * 4 + blockY,
                sliceId, 2);

        for (int blockY = 0; blockY < 2; blockY++)
        for (int blockX = 0; blockX < 2; blockX++)
            _intra8x8Modes.RecordMode(
                macroblockX * 2 + blockX, macroblockY * 2 + blockY,
                sliceId, 2);
    }

    private void RecordIntra4x4MacroblockModesAs8x8(int macroblockX, int macroblockY, int sliceId)
    {
        for (int blockY = 0; blockY < 2; blockY++)
        for (int blockX = 0; blockX < 2; blockX++)
        {
            uint mode = _intra4x4Modes.GetRecordedMode(
                macroblockX * 4 + blockX * 2,
                macroblockY * 4 + blockY * 2,
                sliceId) ?? 2;
            _intra8x8Modes.RecordMode(
                macroblockX * 2 + blockX, macroblockY * 2 + blockY,
                sliceId, mode);
        }
    }

    private byte[] ReconstructChroma(
        byte componentId, int macroblockX, int macroblockY, int sliceId,
        uint mode, H264IntraLumaResidualMacroblock residual,
        int qpY, H264SequenceParameterSet sps, H264PictureParameterSetHeader pps)
    {
        H264IntraChromaResidualComponent component = residual.ChromaComponents
            .SingleOrDefault(item => item.ComponentId == componentId)
            ?? throw new FormatException($"Missing chroma residual component {componentId}.");
        if (component.Blocks.Any(block => !block.IsCoded && block.Residual.TotalCoefficients != 0))
            throw new FormatException("An uncoded chroma AC block cannot contain non-zero coefficients.");
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
            if (!block.IsCoded && block.Residual.TotalCoefficients != 0)
                throw new FormatException("An uncoded luma AC block cannot contain non-zero coefficients.");
            ordered[scanIndex] = block.Residual;
        }
        if (seen.Any(value => !value))
            throw new FormatException("The I_16x16 luma residual block grid is incomplete.");
        return ordered;
    }
}
