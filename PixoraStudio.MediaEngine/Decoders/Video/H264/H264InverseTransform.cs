namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>H.264 integer inverse transforms for residual blocks.</summary>
public static class H264InverseTransform
{
    /// <summary>Applies the normative 4x4 inverse integer transform to a 4x4 matrix.</summary>
    public static int[] Inverse4x4(IReadOnlyList<int> coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        if (coefficients.Count != 16)
            throw new ArgumentException("A 4x4 transform requires exactly 16 coefficients.", nameof(coefficients));

        Span<long> temp = stackalloc long[16];
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            long c0 = coefficients[offset], c1 = coefficients[offset + 1];
            long c2 = coefficients[offset + 2], c3 = coefficients[offset + 3];
            long s0 = c0 + c2;
            long s1 = c0 - c2;
            long s2 = (c1 >> 1) - c3;
            long s3 = c1 + (c3 >> 1);
            temp[offset] = s0 + s3;
            temp[offset + 1] = s1 + s2;
            temp[offset + 2] = s1 - s2;
            temp[offset + 3] = s0 - s3;
        }

        int[] output = new int[16];
        for (int column = 0; column < 4; column++)
        {
            long c0 = temp[column], c1 = temp[4 + column];
            long c2 = temp[8 + column], c3 = temp[12 + column];
            long s0 = c0 + c2;
            long s1 = c0 - c2;
            long s2 = (c1 >> 1) - c3;
            long s3 = c1 + (c3 >> 1);
            output[column] = ToInt32((s0 + s3 + 32) >> 6);
            output[4 + column] = ToInt32((s1 + s2 + 32) >> 6);
            output[8 + column] = ToInt32((s1 - s2 + 32) >> 6);
            output[12 + column] = ToInt32((s0 - s3 + 32) >> 6);
        }
        return output;
    }

    /// <summary>
    /// Applies the normative two-pass 8x8 inverse integer transform.
    /// Coefficients and output samples are row-major (raster) order.
    /// </summary>
    public static int[] Inverse8x8(IReadOnlyList<int> coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        if (coefficients.Count != 64)
            throw new ArgumentException("An 8x8 transform requires exactly 64 coefficients.", nameof(coefficients));

        long[] horizontal = new long[64];
        Span<long> input = stackalloc long[8];
        Span<long> output = stackalloc long[8];

        // First apply the one-dimensional inverse transform to every row.
        for (int row = 0; row < 8; row++)
        {
            int offset = row * 8;
            for (int column = 0; column < 8; column++)
                input[column] = coefficients[offset + column];

            InverseTransform1D(input, output);
            for (int column = 0; column < 8; column++)
                horizontal[offset + column] = output[column];
        }

        // Then apply the same transform to every column and perform
        // the normative final rounding (m + 2^5) >> 6.
        int[] result = new int[64];
        for (int column = 0; column < 8; column++)
        {
            for (int row = 0; row < 8; row++)
                input[row] = horizontal[row * 8 + column];

            InverseTransform1D(input, output);
            for (int row = 0; row < 8; row++)
                result[row * 8 + column] = ToInt32((output[row] + 32) >> 6);
        }

        return result;
    }

    /// <summary>Applies the 2x2 Hadamard transform used for 4:2:0 chroma DC coefficients.</summary>
    public static int[] InverseChromaDc2x2(IReadOnlyList<int> coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        if (coefficients.Count != 4)
            throw new ArgumentException("A 2x2 chroma DC transform requires exactly 4 coefficients.", nameof(coefficients));
        long a = coefficients[0], b = coefficients[1], c = coefficients[2], d = coefficients[3];
        return [ToInt32(a + b + c + d), ToInt32(a - b + c - d),
                ToInt32(a + b - c - d), ToInt32(a - b - c + d)];
    }

    private static void InverseTransform1D(ReadOnlySpan<long> d, Span<long> g)
    {
        long e0 = d[0] + d[4];
        long e1 = -d[3] + d[5] - d[7] - (d[7] >> 1);
        long e2 = d[0] - d[4];
        long e3 = d[1] + d[7] - d[3] - (d[3] >> 1);
        long e4 = (d[2] >> 1) - d[6];
        long e5 = -d[1] + d[7] + d[5] + (d[5] >> 1);
        long e6 = d[2] + (d[6] >> 1);
        long e7 = d[3] + d[5] + d[1] + (d[1] >> 1);

        long f0 = e0 + e6;
        long f1 = e1 + (e7 >> 2);
        long f2 = e2 + e4;
        long f3 = e3 + (e5 >> 2);
        long f4 = e2 - e4;
        long f5 = (e3 >> 2) - e5;
        long f6 = e0 - e6;
        long f7 = e7 - (e1 >> 2);

        g[0] = f0 + f7;
        g[1] = f2 + f5;
        g[2] = f4 + f3;
        g[3] = f6 + f1;
        g[4] = f6 - f1;
        g[5] = f4 - f3;
        g[6] = f2 - f5;
        g[7] = f0 - f7;
    }

    private static int ToInt32(long value)
    {
        if (value is > int.MaxValue or < int.MinValue)
            throw new FormatException("H.264 inverse transform result exceeds Int32.");
        return (int)value;
    }
}
