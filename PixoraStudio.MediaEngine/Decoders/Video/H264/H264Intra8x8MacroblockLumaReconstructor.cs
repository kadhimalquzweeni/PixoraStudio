
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reconstructs the four Intra8x8 luma blocks of an I_NxN macroblock in
/// decoding order, propagating reconstructed samples and resolved modes.
/// </summary>
public sealed class H264Intra8x8MacroblockLumaReconstructor
{
    private static readonly (int X, int Y)[] BlockScan =
        [(0, 0), (1, 0), (0, 1), (1, 1)];

    public byte[] Reconstruct(
        int macroblockX,
        int macroblockY,
        int sliceId,
        IReadOnlyList<byte> externalTop,
        IReadOnlyList<byte> externalLeft,
        byte externalTopLeft,
        bool hasExternalTop,
        bool hasExternalLeft,
        bool hasExternalTopLeft,
        IReadOnlyList<byte> externalTopRight,
        bool hasExternalTopRight,
        H264IntraPredictionModeSyntax predictionModes,
        IReadOnlyList<H264Intra8x8LumaResidualBlock> residualBlocks,
        H264Intra8x8PredictionContext modeContext,
        H264Intra4x4PredictionContext intra4x4ModeContext,
        int qpY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(externalTop);
        ArgumentNullException.ThrowIfNull(externalLeft);
        ArgumentNullException.ThrowIfNull(externalTopRight);
        ArgumentNullException.ThrowIfNull(predictionModes);
        ArgumentNullException.ThrowIfNull(residualBlocks);
        ArgumentNullException.ThrowIfNull(modeContext);
        ArgumentNullException.ThrowIfNull(intra4x4ModeContext);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (macroblockX < 0 || macroblockY < 0 || sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (externalTop.Count != 16 || externalLeft.Count != 16 || externalTopRight.Count != 8)
            throw new ArgumentException("External luma edges must contain sixteen top/left and eight top-right samples.");
        if (!predictionModes.Uses8X8Transform || predictionModes.Modes.Count != 4)
            throw new FormatException("An Intra8x8 macroblock must contain four 8x8 prediction-mode entries.");
        if (residualBlocks.Count != 4)
            throw new FormatException("An Intra8x8 macroblock must contain four residual blocks.");
        if (!pps.Transform8X8ModeFlag)
            throw new FormatException("Intra8x8 reconstruction requires transform_8x8_mode_flag.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (sps.BitDepthLumaMinus8 != 0 || sps.SeparateColourPlaneFlag ||
            sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Intra8x8 reconstruction supports 8-bit 4:2:0 and 4:2:2 luma only.");

        H264Intra8x8LumaResidualBlock[] ordered = OrderResiduals(residualBlocks);
        int qpPrimeY = H264InverseQuantizer.ToQpPrimeY(sps, qpY);
        byte[] output = new byte[256];

        for (int index = 0; index < BlockScan.Length; index++)
        {
            (int blockX, int blockY) = BlockScan[index];
            int pictureBlockX = checked(macroblockX * 2 + blockX);
            int pictureBlockY = checked(macroblockY * 2 + blockY);
            H264IntraPredictionModeEntry entry = predictionModes.Modes[index]
                ?? throw new FormatException("An Intra8x8 prediction-mode entry cannot be null.");
            uint predictedMode = modeContext.GetPredictedMode(
                pictureBlockX, pictureBlockY, sliceId);
            uint mode = H264IntraPredictionModeReader.ResolveMode(
                entry.PreviousModeFlag, entry.RemainderMode, predictedMode);

            byte[] top = new byte[16];
            byte[] left = new byte[8];
            bool hasTop = blockY > 0 || hasExternalTop;
            bool hasLeft = blockX > 0 || hasExternalLeft;
            bool hasTopRight = blockY > 0
                ? blockX == 0
                : blockX == 0 ? hasExternalTop : hasExternalTopRight;

            if (blockY > 0)
            {
                for (int i = 0; i < 16; i++)
                    top[i] = output[7 * 16 + Math.Min(blockX * 8 + i, 15)];
                if (blockX == 1)
                    Array.Fill(top, top[7], 8, 8);
            }
            else if (hasExternalTop)
            {
                if (blockX == 0)
                    for (int i = 0; i < 16; i++) top[i] = externalTop[i];
                else
                {
                    for (int i = 0; i < 8; i++) top[i] = externalTop[8 + i];
                    if (hasExternalTopRight)
                        for (int i = 0; i < 8; i++) top[8 + i] = externalTopRight[i];
                    else
                        Array.Fill(top, top[7], 8, 8);
                }
            }

            if (blockX > 0)
            {
                int sampleY = blockY * 8;
                for (int i = 0; i < 8; i++)
                    left[i] = output[(sampleY + i) * 16 + 7];
            }
            else if (hasExternalLeft)
            {
                for (int i = 0; i < 8; i++)
                    left[i] = externalLeft[blockY * 8 + i];
            }

            byte topLeft;
            bool hasTopLeft;
            if (blockX > 0 && blockY > 0)
            {
                topLeft = output[7 * 16 + 7];
                hasTopLeft = true;
            }
            else if (blockX > 0)
            {
                topLeft = externalTop[7];
                hasTopLeft = hasExternalTop;
            }
            else if (blockY > 0)
            {
                topLeft = externalLeft[7];
                hasTopLeft = hasExternalLeft;
            }
            else
            {
                topLeft = externalTopLeft;
                hasTopLeft = hasExternalTopLeft;
            }

            byte[] prediction = H264Intra8x8Prediction.Predict(
                mode, top, left, topLeft, hasTop, hasLeft,
                hasTopLeft, hasTopRight);
            H264Intra8x8LumaResidualBlock residual = ordered[index];
            if (residual.CoefficientsInScanOrder.Count != 64)
                throw new FormatException("An Intra8x8 residual block must contain 64 scan-order coefficients.");
            if (!residual.IsCoded && residual.CoefficientsInScanOrder.Any(value => value != 0))
                throw new FormatException("An uncoded Intra8x8 residual block cannot contain non-zero coefficients.");

            byte[] reconstructed = H264Intra8x8ResidualReconstructor.Reconstruct(
                prediction, residual.CoefficientsInScanOrder, qpPrimeY, sps, pps);
            for (int y = 0; y < 8; y++)
                Array.Copy(reconstructed, y * 8, output,
                    (blockY * 8 + y) * 16 + blockX * 8, 8);

            modeContext.RecordMode(pictureBlockX, pictureBlockY, sliceId, mode);
            for (int subY = 0; subY < 2; subY++)
            for (int subX = 0; subX < 2; subX++)
                intra4x4ModeContext.RecordMode(
                    macroblockX * 4 + blockX * 2 + subX,
                    macroblockY * 4 + blockY * 2 + subY,
                    sliceId, mode);
        }

        return output;
    }

    private static H264Intra8x8LumaResidualBlock[] OrderResiduals(
        IReadOnlyList<H264Intra8x8LumaResidualBlock> blocks)
    {
        var ordered = new H264Intra8x8LumaResidualBlock[4];
        var seen = new bool[4];
        foreach (H264Intra8x8LumaResidualBlock block in blocks)
        {
            if (block.BlockX is < 0 or > 1 || block.BlockY is < 0 or > 1)
                throw new FormatException("Intra8x8 residual coordinates must be within the macroblock.");
            int index = block.BlockY * 2 + block.BlockX;
            if (seen[index])
                throw new FormatException("Duplicate Intra8x8 residual coordinates.");
            if (block.CoefficientsInScanOrder.Count != 64)
                throw new FormatException("An Intra8x8 residual block must contain 64 coefficients.");
            seen[index] = true;
            ordered[index] = block;
        }
        if (seen.Any(value => !value))
            throw new FormatException("The Intra8x8 residual grid is incomplete.");
        return ordered;
    }
}
