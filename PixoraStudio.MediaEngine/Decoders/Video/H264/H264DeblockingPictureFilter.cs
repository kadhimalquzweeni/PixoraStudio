using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Applies the frame-picture H.264 deblocking process to a fully reconstructed
/// 8-bit 4:2:0 or 4:2:2 surface using retained per-macroblock metadata.
/// </summary>
public static class H264DeblockingPictureFilter
{
    public static int Apply(H264DecodedPictureSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!surface.IsComplete)
            throw new InvalidOperationException("Deblocking requires a fully reconstructed picture.");
        if (!surface.HasCompleteDeblockingMetadata)
            throw new InvalidOperationException("Every macroblock must have deblocking metadata before filtering.");
        if (surface.IsDeblockingFilterApplied)
            throw new InvalidOperationException("The deblocking filter has already been applied to this picture.");

        int filteredSegments = 0;
        Span<byte> luma = surface.GetLumaPlaneSpan();
        Span<byte> cb = surface.GetChromaPlaneSpan(1);
        Span<byte> cr = surface.GetChromaPlaneSpan(2);

        // H.264 processes macroblocks in increasing address order. Within
        // each macroblock it filters all vertical edges from left to right,
        // then all horizontal edges from top to bottom. The horizontal pass
        // therefore consumes samples already modified by this macroblock's
        // vertical pass, while previously processed macroblocks remain filtered.
        for (int macroblockY = 0; macroblockY < surface.MacroblockHeight; macroblockY++)
        for (int macroblockX = 0; macroblockX < surface.MacroblockWidth; macroblockX++)
        {
            H264DeblockingMacroblockMetadata current =
                surface.GetDeblockingMetadataForFiltering(macroblockX, macroblockY);
            if (current.DisableDeblockingFilterIdc == 1)
                continue;

            filteredSegments += FilterVerticalLuma(
                surface, luma, macroblockX, macroblockY, current);
            filteredSegments += FilterVerticalChroma(
                surface, cb, cr, macroblockX, macroblockY, current);
            filteredSegments += FilterHorizontalLuma(
                surface, luma, macroblockX, macroblockY, current);
            filteredSegments += FilterHorizontalChroma(
                surface, cb, cr, macroblockX, macroblockY, current);
        }

        surface.MarkDeblockingFilterApplied();
        return filteredSegments;
    }

    private static int FilterVerticalLuma(
        H264DecodedPictureSurface surface,
        Span<byte> plane,
        int mbX,
        int mbY,
        H264DeblockingMacroblockMetadata current)
    {
        int filtered = 0;
        for (int edge = 0; edge < 4; edge++)
        {
            bool external = edge == 0;
            if (external && mbX == 0)
                continue;
            if (!external && current.TransformSize8x8Flag && edge is 1 or 3)
                continue;

            H264DeblockingMacroblockMetadata pMetadata = current;
            int pSliceId = surface.GetMacroblockSliceId(mbX, mbY);
            if (external)
            {
                pMetadata = surface.GetDeblockingMetadataForFiltering(mbX - 1, mbY);
                pSliceId = surface.GetMacroblockSliceId(mbX - 1, mbY);
                if (SkipExternalEdge(current, pMetadata,
                    surface.GetMacroblockSliceId(mbX, mbY), pSliceId))
                    continue;
            }

            int pColumn = external ? 3 : edge - 1;
            for (int blockRow = 0; blockRow < 4; blockRow++)
            {
                int strength = H264DeblockingBoundaryStrengthDeriver.Derive(
                    pMetadata.LumaBlocks[blockRow * 4 + pColumn],
                    current.LumaBlocks[blockRow * 4 + edge],
                    external);
                if (strength == 0)
                    continue;

                int q0Offset = (mbY * 16 + blockRow * 4) * surface.Width +
                    mbX * 16 + edge * 4;
                if (H264DeblockingFilter.FilterEdge(
                    plane, q0Offset, 1, surface.Width, 4,
                    new H264DeblockingEdgeParameters(
                        strength, pMetadata.QpY, current.QpY,
                        current.AlphaOffsetDiv2, current.BetaOffsetDiv2, false)))
                    filtered++;
            }
        }
        return filtered;
    }

    private static int FilterVerticalChroma(
        H264DecodedPictureSurface surface,
        Span<byte> cb,
        Span<byte> cr,
        int mbX,
        int mbY,
        H264DeblockingMacroblockMetadata current)
    {
        int filtered = 0;
        int chromaRows = surface.ChromaFormatIdc == 1 ? 2 : 4;
        for (int edge = 0; edge < 2; edge++)
        {
            bool external = edge == 0;
            if (external && mbX == 0)
                continue;

            H264DeblockingMacroblockMetadata pMetadata = current;
            if (external)
            {
                pMetadata = surface.GetDeblockingMetadata(mbX - 1, mbY)!;
                int pSlice = surface.GetMacroblockSliceId(mbX - 1, mbY);
                int qSlice = surface.GetMacroblockSliceId(mbX, mbY);
                if (SkipExternalEdge(current, pMetadata, qSlice, pSlice))
                    continue;
            }

            for (int blockRow = 0; blockRow < chromaRows; blockRow++)
            {
                // Chroma bS is inherited from the luma edge containing
                // the corresponding subsampled luma sample. For 4:2:0 one
                // chroma segment spans two luma block rows; the top row is
                // the normative representative for its q0 sample location.
                int lumaBlockRow = surface.ChromaFormatIdc == 1
                    ? blockRow * 2 : blockRow;
                int pLumaColumn = external ? 3 : 1;
                int qLumaColumn = external ? 0 : 2;
                H264DeblockingBlockMetadata pLuma =
                    pMetadata.LumaBlocks[lumaBlockRow * 4 + pLumaColumn];
                H264DeblockingBlockMetadata qLuma =
                    current.LumaBlocks[lumaBlockRow * 4 + qLumaColumn];
                int q0Offset = (mbY * chromaRows * 4 + blockRow * 4) *
                    surface.ChromaWidth + mbX * 8 + edge * 4;

                int strength = H264DeblockingBoundaryStrengthDeriver.Derive(
                    pLuma, qLuma, external);
                if (strength > 0 && H264DeblockingFilter.FilterEdge(
                    cb, q0Offset, 1, surface.ChromaWidth, 4,
                    new H264DeblockingEdgeParameters(
                        strength, pMetadata.QpCb, current.QpCb,
                        current.AlphaOffsetDiv2, current.BetaOffsetDiv2, true)))
                    filtered++;
                if (strength > 0 && H264DeblockingFilter.FilterEdge(
                    cr, q0Offset, 1, surface.ChromaWidth, 4,
                    new H264DeblockingEdgeParameters(
                        strength, pMetadata.QpCr, current.QpCr,
                        current.AlphaOffsetDiv2, current.BetaOffsetDiv2, true)))
                    filtered++;
            }
        }
        return filtered;
    }

    private static int FilterHorizontalLuma(
        H264DecodedPictureSurface surface,
        Span<byte> plane,
        int mbX,
        int mbY,
        H264DeblockingMacroblockMetadata current)
    {
        int filtered = 0;
        for (int edge = 0; edge < 4; edge++)
        {
            bool external = edge == 0;
            if (external && mbY == 0)
                continue;
            if (!external && current.TransformSize8x8Flag && edge is 1 or 3)
                continue;

            H264DeblockingMacroblockMetadata pMetadata = current;
            if (external)
            {
                pMetadata = surface.GetDeblockingMetadataForFiltering(mbX, mbY - 1);
                int pSlice = surface.GetMacroblockSliceId(mbX, mbY - 1);
                int qSlice = surface.GetMacroblockSliceId(mbX, mbY);
                if (SkipExternalEdge(current, pMetadata, qSlice, pSlice))
                    continue;
            }

            int pRow = external ? 3 : edge - 1;
            for (int blockColumn = 0; blockColumn < 4; blockColumn++)
            {
                int strength = H264DeblockingBoundaryStrengthDeriver.Derive(
                    pMetadata.LumaBlocks[pRow * 4 + blockColumn],
                    current.LumaBlocks[edge * 4 + blockColumn],
                    external);
                if (strength == 0)
                    continue;

                int q0Offset = (mbY * 16 + edge * 4) * surface.Width +
                    mbX * 16 + blockColumn * 4;
                if (H264DeblockingFilter.FilterEdge(
                    plane, q0Offset, surface.Width, 1, 4,
                    new H264DeblockingEdgeParameters(
                        strength, pMetadata.QpY, current.QpY,
                        current.AlphaOffsetDiv2, current.BetaOffsetDiv2, false)))
                    filtered++;
            }
        }
        return filtered;
    }

    private static int FilterHorizontalChroma(
        H264DecodedPictureSurface surface,
        Span<byte> cb,
        Span<byte> cr,
        int mbX,
        int mbY,
        H264DeblockingMacroblockMetadata current)
    {
        int filtered = 0;
        int chromaRows = surface.ChromaFormatIdc == 1 ? 2 : 4;
        for (int edge = 0; edge < chromaRows; edge++)
        {
            bool external = edge == 0;
            if (external && mbY == 0)
                continue;

            H264DeblockingMacroblockMetadata pMetadata = current;
            if (external)
            {
                pMetadata = surface.GetDeblockingMetadata(mbX, mbY - 1)!;
                int pSlice = surface.GetMacroblockSliceId(mbX, mbY - 1);
                int qSlice = surface.GetMacroblockSliceId(mbX, mbY);
                if (SkipExternalEdge(current, pMetadata, qSlice, pSlice))
                    continue;
            }

            for (int blockColumn = 0; blockColumn < 2; blockColumn++)
            {
                // Map the chroma q0 sample to its corresponding luma
                // block edge (SubWidthC=2; SubHeightC=2 for 4:2:0, 1 for
                // 4:2:2) as required by the H.264 chroma bS derivation.
                int lumaBlockColumn = blockColumn * 2;
                int pLumaRow = external
                    ? 3 : surface.ChromaFormatIdc == 1 ? 1 : edge - 1;
                int qLumaRow = external
                    ? 0 : surface.ChromaFormatIdc == 1 ? 2 : edge;
                H264DeblockingBlockMetadata pLuma =
                    pMetadata.LumaBlocks[pLumaRow * 4 + lumaBlockColumn];
                H264DeblockingBlockMetadata qLuma =
                    current.LumaBlocks[qLumaRow * 4 + lumaBlockColumn];
                int q0Offset = (mbY * chromaRows * 4 + edge * 4) *
                    surface.ChromaWidth + mbX * 8 + blockColumn * 4;

                int strength = H264DeblockingBoundaryStrengthDeriver.Derive(
                    pLuma, qLuma, external);
                if (strength > 0 && H264DeblockingFilter.FilterEdge(
                    cb, q0Offset, surface.ChromaWidth, 1, 4,
                    new H264DeblockingEdgeParameters(
                        strength, pMetadata.QpCb, current.QpCb,
                        current.AlphaOffsetDiv2, current.BetaOffsetDiv2, true)))
                    filtered++;
                if (strength > 0 && H264DeblockingFilter.FilterEdge(
                    cr, q0Offset, surface.ChromaWidth, 1, 4,
                    new H264DeblockingEdgeParameters(
                        strength, pMetadata.QpCr, current.QpCr,
                        current.AlphaOffsetDiv2, current.BetaOffsetDiv2, true)))
                    filtered++;
            }
        }
        return filtered;
    }

    private static bool SkipExternalEdge(
        H264DeblockingMacroblockMetadata current,
        H264DeblockingMacroblockMetadata previous,
        int currentSliceId,
        int previousSliceId)
    {
        // The current (q-side) macroblock controls this edge. idc=1
        // disables all filtering for its slice; idc=2 disables only edges
        // whose p-side neighbour belongs to another slice.
        if (current.DisableDeblockingFilterIdc == 1)
            return true;
        return current.DisableDeblockingFilterIdc == 2 &&
            currentSliceId != previousSliceId;
    }
}
