using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Adds decoded inter residual coefficients to a complete 16x16 macroblock
/// prediction. The caller assembles motion-compensated partitions into one
/// macroblock prediction before invoking this stage.
/// </summary>
public static class H264InterMacroblockResidualReconstructor
{
    public static H264MotionCompensatedBlock Reconstruct(
        H264MotionCompensatedBlock prediction,
        H264InterResidualMacroblock residual,
        int qpY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(residual);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (!sps.FrameMbsOnlyFlag || sps.SeparateColourPlaneFlag ||
            sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0 ||
            sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Inter residual reconstruction supports frame-only 8-bit non-separate 4:2:0 and 4:2:2 only.");
        if (pps.EntropyCodingModeFlag)
            throw new NotSupportedException("CABAC inter residual reconstruction is not implemented.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (qpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(qpY));

        int chromaHeight = sps.ChromaFormatIdc == 1 ? 8 : 16;
        if (prediction.LumaWidth != 16 || prediction.LumaHeight != 16 ||
            prediction.Luma.Length != 256 ||
            prediction.ChromaWidth != 8 || prediction.ChromaHeight != chromaHeight ||
            prediction.Cb.Length != 8 * chromaHeight ||
            prediction.Cr.Length != 8 * chromaHeight)
            throw new ArgumentException("Prediction buffers must describe one complete 16x16 macroblock.", nameof(prediction));

        if (residual.LumaBlocks.Count != 16)
            throw new FormatException("An inter macroblock must contain sixteen luma 4x4 residual blocks.");
        if (residual.ChromaComponents.Count != 2 ||
            residual.ChromaComponents.Count(component => component.ComponentId == 1) != 1 ||
            residual.ChromaComponents.Count(component => component.ComponentId == 2) != 1)
            throw new FormatException("Inter residual data must contain exactly one Cb and one Cr component.");

        byte[] luma = (byte[])prediction.Luma.Clone();
        bool[] seen = new bool[16];
        foreach (H264IntraLumaResidualBlock block in residual.LumaBlocks)
        {
            if (block.BlockX is < 0 or > 3 || block.BlockY is < 0 or > 3)
                throw new FormatException("Luma residual coordinates must be within the macroblock.");
            int index = block.BlockY * 4 + block.BlockX;
            if (seen[index])
                throw new FormatException("Duplicate luma residual block coordinates.");
            seen[index] = true;
            if (block.Residual.MaximumCoefficients != 16 ||
                block.Residual.Coefficients.Count != 16)
                throw new FormatException("Inter luma residual blocks must contain sixteen coefficients.");
            if (!block.IsCoded && block.Residual.TotalCoefficients != 0)
                throw new FormatException("An uncoded luma block cannot contain non-zero coefficients.");

            byte[] blockPrediction = ExtractLumaBlock(prediction.Luma, block.BlockX, block.BlockY);
            byte[] reconstructed = H264IntraResidualReconstructor.Reconstruct4x4(
                blockPrediction, block.Residual, qpY, sps, pps, scalingListIndex: 3);
            WriteLumaBlock(luma, reconstructed, block.BlockX, block.BlockY);
        }
        if (seen.Any(value => !value))
            throw new FormatException("The inter luma residual block grid is incomplete.");

        H264IntraChromaResidualComponent cbResidual = residual.ChromaComponents.Single(c => c.ComponentId == 1);
        H264IntraChromaResidualComponent crResidual = residual.ChromaComponents.Single(c => c.ComponentId == 2);
        byte[] cb = H264IntraChromaResidualReconstructor.Reconstruct(
            prediction.Cb, cbResidual, qpY, sps, pps, scalingListIndexOverride: 4);
        byte[] cr = H264IntraChromaResidualReconstructor.Reconstruct(
            prediction.Cr, crResidual, qpY, sps, pps, scalingListIndexOverride: 5);

        return new H264MotionCompensatedBlock(16, 16, 8, chromaHeight, luma, cb, cr);
    }

    private static byte[] ExtractLumaBlock(IReadOnlyList<byte> plane, int blockX, int blockY)
    {
        byte[] block = new byte[16];
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            block[y * 4 + x] = plane[(blockY * 4 + y) * 16 + blockX * 4 + x];
        return block;
    }

    private static void WriteLumaBlock(
        byte[] destination, IReadOnlyList<byte> block, int blockX, int blockY)
    {
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            destination[(blockY * 4 + y) * 16 + blockX * 4 + x] = block[y * 4 + x];
    }
}
