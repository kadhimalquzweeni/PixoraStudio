using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reconstructs an H.264 Intra16x16 luma macroblock from its separately coded
/// DC block and sixteen AC blocks. AC blocks are supplied in H.264 luma4x4BlkIdx
/// order; output samples are returned in 16x16 raster order.
/// </summary>
public static class H264Intra16x16ResidualReconstructor
{
    // luma4x4BlkIdx to (4x4 block row, 4x4 block column), per macroblock
    // 8x8-group scan order.
    private static readonly int[] BlockScanToRaster =
    [
        0, 1, 4, 5,
        2, 3, 6, 7,
        8, 9, 12, 13,
        10, 11, 14, 15
    ];


    /// <summary>
    /// Reconstructs one 16x16 luma macroblock. dcResidual contains the sixteen
    /// Intra16x16 luma DC levels in 4x4 zig-zag scan order. acResidualBlocks
    /// contains exactly sixteen 15-coefficient AC blocks in luma4x4BlkIdx order.
    /// qpPrimeY is QPY-prime (including the bit-depth offset).
    /// </summary>
    public static byte[] Reconstruct(
        IReadOnlyList<byte> prediction,
        H264CavlcResidualBlock dcResidual,
        IReadOnlyList<H264CavlcResidualBlock> acResidualBlocks,
        int qpPrimeY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        int scalingListIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(dcResidual);
        ArgumentNullException.ThrowIfNull(acResidualBlocks);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (prediction.Count != 256)
            throw new ArgumentException("An Intra16x16 prediction must contain exactly 256 samples.", nameof(prediction));
        if (acResidualBlocks.Count != 16)
            throw new ArgumentException("An Intra16x16 macroblock must contain exactly sixteen AC blocks.", nameof(acResidualBlocks));
        if (dcResidual.MaximumCoefficients != 16 || dcResidual.Coefficients.Count != 16)
            throw new ArgumentException("The Intra16x16 DC block must contain exactly sixteen coefficient levels.", nameof(dcResidual));
        ValidateResidual(dcResidual, 16, nameof(dcResidual));

        int[] dcScan = dcResidual.Coefficients.ToArray();
        int[] dcRaster = H264CoefficientScanner.ToRasterOrder(dcScan);
        int[] transformedDc = InverseHadamard4x4(dcRaster);

        // H.264 8.5.9 applies the inverse Hadamard first, then the
        // dedicated Intra16x16 DC scaling with its six-bit normalisation.
        int[] dcDequantized = H264InverseQuantizer.DequantizeIntra16x16Dc(
            transformedDc, qpPrimeY, sps, pps, scalingListIndex);

        byte[] output = new byte[256];
        for (int scanBlock = 0; scanBlock < 16; scanBlock++)
        {
            H264CavlcResidualBlock ac = acResidualBlocks[scanBlock]
                ?? throw new ArgumentException("An AC residual block cannot be null.", nameof(acResidualBlocks));
            ValidateResidual(ac, 15, $"acResidualBlocks[{scanBlock}]");

            int[] coefficients = new int[16];
            coefficients[0] = dcDequantized[BlockScanToRaster[scanBlock]];

            // Intra16x16 AC syntax omits scan position zero. The remaining
            // fifteen levels occupy 4x4 scan positions one through fifteen.
            for (int i = 0; i < 15; i++)
                coefficients[i + 1] = ac.Coefficients[i];

            int[] raster = H264CoefficientScanner.ToRasterOrder(coefficients);
            int[] dequantizedAc = H264InverseQuantizer.Dequantize4x4(
                raster, qpPrimeY, sps, pps, scalingListIndex);

            // The DC coefficient has already used the special Intra16x16
            // scaling rule, so replace the ordinary dequantised DC value.
            dequantizedAc[0] = dcDequantized[BlockScanToRaster[scanBlock]];
            int[] spatialResidual = H264InverseTransform.Inverse4x4(dequantizedAc);

            int blockRaster = BlockScanToRaster[scanBlock];
            int blockRow = blockRaster / 4;
            int blockColumn = blockRaster % 4;
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int destination = (blockRow * 4 + y) * 16 + blockColumn * 4 + x;
                    output[destination] = (byte)Math.Clamp(
                        prediction[destination] + (long)spatialResidual[y * 4 + x], 0, 255);
                }
            }
        }

        return output;
    }

    /// <summary>Applies the unnormalised 4x4 Hadamard transform to DC levels in raster order.</summary>
    public static int[] InverseHadamard4x4(IReadOnlyList<int> coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        if (coefficients.Count != 16)
            throw new ArgumentException("The Intra16x16 DC Hadamard transform requires exactly sixteen coefficients.", nameof(coefficients));

        long[] horizontal = new long[16];
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            long a = coefficients[offset], b = coefficients[offset + 1];
            long c = coefficients[offset + 2], d = coefficients[offset + 3];
            horizontal[offset] = a + b + c + d;
            horizontal[offset + 1] = a + b - c - d;
            horizontal[offset + 2] = a - b - c + d;
            horizontal[offset + 3] = a - b + c - d;
        }

        int[] result = new int[16];
        for (int column = 0; column < 4; column++)
        {
            long a = horizontal[column], b = horizontal[4 + column];
            long c = horizontal[8 + column], d = horizontal[12 + column];
            result[column] = ToInt32(a + b + c + d);
            result[4 + column] = ToInt32(a + b - c - d);
            result[8 + column] = ToInt32(a - b - c + d);
            result[12 + column] = ToInt32(a - b + c - d);
        }
        return result;
    }

    private static void ValidateResidual(H264CavlcResidualBlock residual, int capacity, string parameterName)
    {
        if (residual.MaximumCoefficients != capacity || residual.Coefficients.Count != capacity)
            throw new ArgumentException($"The residual block must contain exactly {capacity} coefficient levels.", parameterName);
        if (residual.TotalCoefficients is < 0 || residual.TotalCoefficients > capacity)
            throw new FormatException("The residual TotalCoeff is outside the block capacity.");
        if (residual.Coefficients.Count(value => value != 0) != residual.TotalCoefficients)
            throw new FormatException("The residual TotalCoeff does not match the number of non-zero coefficient levels.");
    }

    private static int ToInt32(long value)
    {
        if (value is > int.MaxValue or < int.MinValue)
            throw new FormatException("Intra16x16 DC transform result exceeds Int32.");
        return (int)value;
    }
}
