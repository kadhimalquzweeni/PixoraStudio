using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reconstructs the luma plane of an I_NxN macroblock using sixteen
/// Intra4x4 prediction blocks. Blocks are processed in H.264 macroblock
/// decoding order so reconstructed samples become references immediately.
/// </summary>
public sealed class H264Intra4x4MacroblockLumaReconstructor
{
    private static readonly (int X, int Y)[] BlockScan =
    [
        (0, 0), (1, 0), (0, 1), (1, 1),
        (2, 0), (3, 0), (2, 1), (3, 1),
        (0, 2), (1, 2), (0, 3), (1, 3),
        (2, 2), (3, 2), (2, 3), (3, 3)
    ];

    /// <summary>
    /// Reconstructs one 16x16 luma macroblock. top/left are the sixteen
    /// external edge samples; topLeft and availability describe the external
    /// top-left neighbour. The mode context is retained across macroblocks
    /// in a picture and must be reset at each new picture.
    /// </summary>
    public byte[] Reconstruct(
        int macroblockX,
        int macroblockY,
        int sliceId,
        IReadOnlyList<byte> top,
        IReadOnlyList<byte> left,
        byte topLeft,
        bool hasTop,
        bool hasLeft,
        bool hasTopLeft,
        H264IntraPredictionModeSyntax predictionModes,
        IReadOnlyList<H264IntraLumaResidualBlock> residualBlocks,
        H264Intra4x4PredictionContext modeContext,
        int qpY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(predictionModes);
        ArgumentNullException.ThrowIfNull(residualBlocks);
        ArgumentNullException.ThrowIfNull(modeContext);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (macroblockX < 0 || macroblockY < 0 || sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (top.Count != 16 || left.Count != 16)
            throw new ArgumentException("External luma edges must each contain sixteen samples.");
        if (predictionModes.Uses8X8Transform || predictionModes.Modes.Count != 16)
            throw new NotSupportedException("This path reconstructs sixteen Intra4x4 blocks only.");
        if (residualBlocks.Count != 16)
            throw new FormatException("An I_NxN Intra4x4 macroblock must contain sixteen luma residual blocks.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (sps.BitDepthLumaMinus8 != 0)
            throw new NotSupportedException("This Intra4x4 path currently supports 8-bit luma only.");

        int qpPrimeY = H264InverseQuantizer.ToQpPrimeY(sps, qpY);
        H264IntraLumaResidualBlock[] orderedResiduals = OrderResiduals(residualBlocks);
        byte[] output = new byte[256];

        for (int blockIndex = 0; blockIndex < BlockScan.Length; blockIndex++)
        {
            (int blockX, int blockY) = BlockScan[blockIndex];
            int pictureBlockX = checked(macroblockX * 4 + blockX);
            int pictureBlockY = checked(macroblockY * 4 + blockY);
            H264IntraPredictionModeEntry entry = predictionModes.Modes[blockIndex]
                ?? throw new FormatException("An Intra4x4 prediction-mode entry cannot be null.");

            uint predictedMode = modeContext.GetPredictedMode(
                pictureBlockX, pictureBlockY, sliceId);
            uint mode = H264IntraPredictionModeReader.ResolveMode(
                entry.PreviousModeFlag, entry.RemainderMode, predictedMode);

            bool blockHasTop = blockY > 0 || hasTop;
            bool blockHasLeft = blockX > 0 || hasLeft;
            bool blockHasTopLeft = blockX > 0 && blockY > 0
                ? true
                : blockX > 0
                    ? blockY > 0 || hasTop
                    : blockY > 0
                        ? hasLeft
                        : hasTopLeft;

            byte[] blockTop = new byte[8];
            byte[] blockLeft = new byte[4];
            bool hasTopRight = blockX < 3 || blockY == 0 && (blockX * 4 + 7 < 16) && hasTop;

            for (int i = 0; i < 8; i++)
            {
                int sampleX = blockX * 4 + i;
                if (blockY > 0)
                {
                    if (sampleX < 16)
                        blockTop[i] = output[(blockY * 4 - 1) * 16 + sampleX];
                    else
                        blockTop[i] = blockTop[3];
                }
                else if (sampleX < 16 && hasTop)
                {
                    blockTop[i] = top[sampleX];
                }
                else
                {
                    blockTop[i] = blockTop[3];
                }
            }

            for (int i = 0; i < 4; i++)
            {
                int sampleY = blockY * 4 + i;
                blockLeft[i] = blockX > 0
                    ? output[sampleY * 16 + blockX * 4 - 1]
                    : hasLeft ? left[sampleY] : (byte)0;
            }

            byte blockTopLeft;
            if (blockX > 0 && blockY > 0)
                blockTopLeft = output[(blockY * 4 - 1) * 16 + blockX * 4 - 1];
            else if (blockX > 0)
                blockTopLeft = hasTop ? top[blockX * 4 - 1] : (byte)0;
            else if (blockY > 0)
                blockTopLeft = hasLeft ? left[blockY * 4 - 1] : (byte)0;
            else
                blockTopLeft = hasTopLeft ? topLeft : (byte)0;

            if (mode is 0 or 3 or 7 && !blockHasTop ||
                mode is 1 or 8 && !blockHasLeft ||
                mode is 4 or 5 or 6 &&
                    (!blockHasTop || !blockHasLeft || !blockHasTopLeft))
                throw new FormatException("The selected Intra4x4 mode requires unavailable reference samples.");

            byte[] prediction = H264IntraPrediction.Predict4x4(
                mode, blockTop, blockLeft, blockTopLeft,
                blockHasTop, blockHasLeft, blockHasTopLeft, hasTopRight);
            H264IntraLumaResidualBlock residual = orderedResiduals[blockIndex];
            if (!residual.IsCoded && residual.Residual.TotalCoefficients != 0)
                throw new FormatException("An uncoded luma block cannot contain non-zero coefficients.");

            byte[] reconstructed = H264IntraResidualReconstructor.Reconstruct4x4(
                prediction, residual.Residual, qpPrimeY, sps, pps);
            for (int y = 0; y < 4; y++)
                Array.Copy(reconstructed, y * 4, output,
                    (blockY * 4 + y) * 16 + blockX * 4, 4);

            modeContext.RecordMode(pictureBlockX, pictureBlockY, sliceId, mode);
        }

        return output;
    }

    private static H264IntraLumaResidualBlock[] OrderResiduals(
        IReadOnlyList<H264IntraLumaResidualBlock> blocks)
    {
        var ordered = new H264IntraLumaResidualBlock[16];
        var seen = new bool[16];
        for (int i = 0; i < blocks.Count; i++)
        {
            H264IntraLumaResidualBlock block = blocks[i]
                ?? throw new FormatException("A luma residual block cannot be null.");
            if (block.BlockX is < 0 or > 3 || block.BlockY is < 0 or > 3)
                throw new FormatException("Luma residual block coordinates must be within the macroblock.");
            int index = Array.IndexOf(BlockScan, (block.BlockX, block.BlockY));
            if (index < 0 || seen[index])
                throw new FormatException("Luma residual block coordinates are duplicated or invalid.");
            if (block.Residual.MaximumCoefficients != 16 ||
                block.Residual.Coefficients.Count != 16 ||
                block.Residual.TotalCoefficients is < 0 or > 16 ||
                block.Residual.Coefficients.Count(value => value != 0) != block.Residual.TotalCoefficients)
                throw new FormatException("An Intra4x4 residual block must contain sixteen consistent coefficient levels.");
            seen[index] = true;
            ordered[index] = block;
        }
        if (seen.Any(value => !value))
            throw new FormatException("The Intra4x4 residual grid is incomplete.");
        return ordered;
    }
}
