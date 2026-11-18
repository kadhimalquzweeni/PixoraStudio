namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>H.264 integer inverse transforms for 4x4 residual blocks.</summary>
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

    private static int ToInt32(long value)
    {
        if (value is > int.MaxValue or < int.MinValue)
            throw new FormatException("H.264 inverse transform result exceeds Int32.");
        return (int)value;
    }
}
