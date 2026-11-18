using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reconstructs 8-bit 4:2:0 and 4:2:2 Cb/Cr macroblock residuals according
/// to H.264 clauses 8.5.4, 8.5.8, and 8.5.11–8.5.12.
/// </summary>
public static class H264IntraChromaResidualReconstructor
{
    private static readonly int[,] LevelScale =
    {
        { 10, 11, 13, 14, 16, 18 },
        { 16, 18, 20, 23, 25, 29 },
        { 13, 14, 16, 18, 20, 23 }
    };

    private static readonly int[] ChromaQpMap =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
        10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
        20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
        29, 30, 31, 32, 32, 33, 34, 34, 35, 35,
        36, 36, 37, 37, 37, 38, 38, 38, 39, 39,
        39, 39
    ];

    /// <summary>Derives QP'c for Cb (componentId 1) or Cr (componentId 2), for 8-bit streams.</summary>
    public static int CalculateChromaQpPrime(
        int qpY,
        byte componentId,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        if (componentId is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(componentId));
        if (sps.BitDepthChromaMinus8 != 0)
            throw new NotSupportedException("This chroma reconstructor currently supports 8-bit chroma only.");
        if (sps.SeparateColourPlaneFlag || sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Only non-separate 4:2:0 and 4:2:2 chroma are supported.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new FormatException("The PPS references a different SPS.");
        if (qpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(qpY), "For 8-bit streams QPY must be in the range 0..51.");

        int offset = componentId == 1
            ? pps.ChromaQpIndexOffset
            : pps.SecondChromaQpIndexOffset ?? pps.ChromaQpIndexOffset;
        int qpi = Math.Clamp(qpY + offset, 0, 51);
        return ChromaQpMap[qpi];
    }

    /// <summary>
    /// Reconstructs one 8-bit chroma component. Prediction is 8x8 for 4:2:0
    /// and 8x16 for 4:2:2, both in row-major order. qpY is SliceQPY.
    /// </summary>
    public static byte[] Reconstruct(
        IReadOnlyList<byte> prediction,
        H264IntraChromaResidualComponent component,
        int qpY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        int blocksHigh = sps.ChromaFormatIdc switch
        {
            1 => 2,
            2 => 4,
            _ => throw new NotSupportedException("Only 4:2:0 and 4:2:2 chroma are supported.")
        };
        if (sps.SeparateColourPlaneFlag || sps.BitDepthChromaMinus8 != 0)
            throw new NotSupportedException("Only non-separate 8-bit chroma is supported.");
        if (component.ComponentId is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(component), "ComponentId must be 1 (Cb) or 2 (Cr).");

        int width = 8;
        int height = blocksHigh * 4;
        if (prediction.Count != width * height)
            throw new ArgumentException($"Chroma prediction must contain exactly {width * height} samples.", nameof(prediction));

        int blockCount = 2 * blocksHigh;
        if (component.Blocks.Count != blockCount)
            throw new ArgumentException($"The component must contain exactly {blockCount} AC blocks.", nameof(component));

        int qpPrimeC = CalculateChromaQpPrime(qpY, component.ComponentId, sps, pps);
        int scalingListIndex = component.ComponentId; // Intra Cb=1, Cr=2.
        int[] dc = DecodeAndScaleDc(component.Dc, blocksHigh, qpPrimeC, sps, pps, scalingListIndex);

        byte[] output = new byte[width * height];
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            H264IntraChromaResidualBlock block = component.Blocks[blockIndex]
                ?? throw new ArgumentException("A chroma AC block cannot be null.", nameof(component.Blocks));
            if (block.BlockX != blockIndex % 2 || block.BlockY != blockIndex / 2)
                throw new FormatException("Chroma AC blocks must be supplied in raster block order.");
            ValidateResidual(block.Residual, 15, $"component.Blocks[{blockIndex}]");

            int[] scan = new int[16];
            scan[0] = dc[blockIndex];
            for (int i = 0; i < 15; i++)
                scan[i + 1] = block.Residual.Coefficients[i];

            int[] raster = H264CoefficientScanner.ToRasterOrder(scan);
            int[] scaled = H264InverseQuantizer.Dequantize4x4(
                raster, qpPrimeC, sps, pps, scalingListIndex);
            scaled[0] = dc[blockIndex];
            int[] residual = H264InverseTransform.Inverse4x4(scaled);

            int blockX = block.BlockX * 4;
            int blockY = block.BlockY * 4;
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int destination = (blockY + y) * width + blockX + x;
                output[destination] = (byte)Math.Clamp(
                    prediction[destination] + (long)residual[y * 4 + x], 0, 255);
            }
        }

        return output;
    }

    /// <summary>Performs the 2x2 inverse Hadamard transform for 4:2:0 chroma DC.</summary>
    public static int[] InverseChromaDc2x2(IReadOnlyList<int> rasterCoefficients)
    {
        ArgumentNullException.ThrowIfNull(rasterCoefficients);
        if (rasterCoefficients.Count != 4)
            throw new ArgumentException("A 2x2 chroma DC transform requires four coefficients.", nameof(rasterCoefficients));

        long a = rasterCoefficients[0], b = rasterCoefficients[1];
        long c = rasterCoefficients[2], d = rasterCoefficients[3];
        return
        [
            ToInt32(a + b + c + d),
            ToInt32(a - b + c - d),
            ToInt32(a + b - c - d),
            ToInt32(a - b - c + d)
        ];
    }

    /// <summary>Performs the 2x4 inverse Hadamard transform for 4:2:2 chroma DC.</summary>
    public static int[] InverseChromaDc2x4(IReadOnlyList<int> rasterCoefficients)
    {
        ArgumentNullException.ThrowIfNull(rasterCoefficients);
        if (rasterCoefficients.Count != 8)
            throw new ArgumentException("A 2x4 chroma DC transform requires eight coefficients.", nameof(rasterCoefficients));

        int[,] vertical =
        {
            { 1, 1, 1, 1 },
            { 1, 1, -1, -1 },
            { 1, -1, -1, 1 },
            { 1, -1, 1, -1 }
        };

        int[] result = new int[8];
        for (int y = 0; y < 4; y++)
        {
            long sum = 0;
            long difference = 0;
            for (int row = 0; row < 4; row++)
            {
                sum += (long)vertical[y, row] * rasterCoefficients[row * 2];
                difference += (long)vertical[y, row] * rasterCoefficients[row * 2 + 1];
            }
            result[y * 2] = ToInt32(sum + difference);
            result[y * 2 + 1] = ToInt32(sum - difference);
        }
        return result;
    }

    private static int[] DecodeAndScaleDc(
        H264CavlcResidualBlock? dcResidual,
        int blocksHigh,
        int qpPrimeC,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        int scalingListIndex)
    {
        int count = blocksHigh * 2;
        int[] levels = new int[count];
        if (dcResidual is not null)
        {
            ValidateResidual(dcResidual, count, nameof(dcResidual));
            if (blocksHigh == 2)
            {
                Array.Copy(dcResidual.Coefficients, levels, 4);
            }
            else
            {
                // Table 8-305: inverse raster scan for the 2x4 DC coefficient array.
                int[] source = dcResidual.Coefficients.ToArray();
                levels =
                [
                    source[0], source[2],
                    source[1], source[5],
                    source[3], source[6],
                    source[4], source[7]
                ];
            }
        }

        int[] transformed = blocksHigh == 2
            ? InverseChromaDc2x2(levels)
            : InverseChromaDc2x4(levels);

        int scalingList0 = H264ScalingMatrixResolver.Resolve4x4(sps, pps)[scalingListIndex][0];
        long scale = (long)LevelScale[0, qpPrimeC % 6] * scalingList0;
        int[] result = new int[count];

        if (blocksHigh == 2)
        {
            int shift = qpPrimeC / 6;
            for (int i = 0; i < count; i++)
            {
                long value = checked((long)transformed[i] * scale);
                value = checked(value << shift) >> 5;
                result[i] = ToInt32(value);
            }
        }
        else
        {
            int qpDc = qpPrimeC + 3;
            int qpDiv6 = qpDc / 6;
            int qpMod6 = qpDc % 6;
            long dcScale = (long)LevelScale[0, qpMod6] * scalingList0;
            for (int i = 0; i < count; i++)
            {
                long product = checked((long)transformed[i] * dcScale);
                long value = qpDc >= 36
                    ? checked(product << (qpDc / 6 - 6))
                    : (product + (1L << (5 - qpDiv6))) >> (6 - qpDiv6);
                result[i] = ToInt32(value);
            }
        }

        return result;
    }

    private static void ValidateResidual(H264CavlcResidualBlock residual, int capacity, string parameterName)
    {
        if (residual.MaximumCoefficients != capacity || residual.Coefficients.Count != capacity)
            throw new ArgumentException($"The residual block must contain exactly {capacity} coefficient levels.", parameterName);
        if (residual.TotalCoefficients < 0 || residual.TotalCoefficients > capacity)
            throw new FormatException("The residual TotalCoeff is outside the block capacity.");
        if (residual.Coefficients.Count(value => value != 0) != residual.TotalCoefficients)
            throw new FormatException("The residual TotalCoeff does not match the number of non-zero coefficient levels.");
    }

    private static int ToInt32(long value)
    {
        if (value is > int.MaxValue or < int.MinValue)
            throw new FormatException("Chroma reconstruction intermediate exceeds Int32.");
        return (int)value;
    }
}
