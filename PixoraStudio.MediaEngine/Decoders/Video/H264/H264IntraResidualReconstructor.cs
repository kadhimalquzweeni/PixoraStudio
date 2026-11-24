using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reconstructs one 4x4 luma block by mapping CAVLC scan-order levels to
/// raster order, inverse quantising, applying the integer inverse transform
/// and adding the resulting residual to the intra prediction samples.
/// A 15-coefficient block is treated as Intra16x16 AC (its DC term is zero
/// here and must be reconstructed by the separate Intra16x16 DC path).
/// </summary>
public static class H264IntraResidualReconstructor
{
    public static byte[] Reconstruct4x4(
        IReadOnlyList<byte> prediction,
        H264CavlcResidualBlock residual,
        int qpPrimeY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        int scalingListIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(residual);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (prediction.Count != 16)
            throw new ArgumentException("A 4x4 prediction block must contain exactly 16 samples.", nameof(prediction));
        if (residual.MaximumCoefficients is not (15 or 16) ||
            residual.Coefficients.Count != residual.MaximumCoefficients)
            throw new ArgumentException("The residual must contain a valid 15-coefficient AC or 16-coefficient block.", nameof(residual));
        if (residual.TotalCoefficients < 0 ||
            residual.TotalCoefficients > residual.MaximumCoefficients)
            throw new FormatException("The residual TotalCoeff is outside the block capacity.");

        int[] scanOrder = new int[16];
        if (residual.MaximumCoefficients == 16)
        {
            for (int i = 0; i < 16; i++)
                scanOrder[i] = residual.Coefficients[i];
        }
        else
        {
            // Intra16x16 luma AC syntax omits coefficient position zero.
            for (int i = 0; i < 15; i++)
                scanOrder[i + 1] = residual.Coefficients[i];
        }

        int[] rasterOrder = H264CoefficientScanner.ToRasterOrder(scanOrder);
        int[] dequantized = H264InverseQuantizer.Dequantize4x4(
            rasterOrder, qpPrimeY, sps, pps, scalingListIndex);
        int[] spatialResidual = H264InverseTransform.Inverse4x4(dequantized);
        return H264IntraPrediction.AddResidual(prediction, spatialResidual);
    }
}
