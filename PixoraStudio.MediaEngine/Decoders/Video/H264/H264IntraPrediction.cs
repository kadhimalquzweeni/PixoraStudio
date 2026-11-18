namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Implements the H.264 Intra_4x4 and Intra_16x16 luma prediction equations.
/// Reference samples are supplied by the caller in picture order; availability
/// must already account for picture, slice and constrained-intra boundaries.
/// </summary>
public static class H264IntraPrediction
{
    /// <summary>
    /// Predicts a 4x4 luma block. top contains T0..T7, left contains L0..L3.
    /// Unavailable top-right samples are replaced by T3 where required.
    /// </summary>
    public static byte[] Predict4x4(
        uint mode,
        IReadOnlyList<byte> top,
        IReadOnlyList<byte> left,
        byte topLeft,
        bool hasTop,
        bool hasLeft,
        bool hasTopLeft,
        bool hasTopRight = true)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(left);
        if (top.Count < 4 || left.Count < 4)
            throw new ArgumentException("Intra4x4 prediction requires four top and four left samples.");
        if (mode > 8)
            throw new ArgumentOutOfRangeException(nameof(mode));

        RequireReferences(mode, hasTop, hasLeft, hasTopLeft);
        if (hasTopRight && (mode is 3 or 7) && top.Count < 8)
            throw new ArgumentException("This prediction mode requires all eight top reference samples when top-right is available.", nameof(top));
        byte[] t = ExtendTop(top, hasTopRight);
        byte[] l = ExtendLeft(left);
        byte[] result = new byte[16];

        switch (mode)
        {
            case 0: // Vertical
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        result[y * 4 + x] = t[x];
                break;

            case 1: // Horizontal
                for (int y = 0; y < 4; y++)
                    Array.Fill(result, l[y], y * 4, 4);
                break;

            case 2: // DC
                FillDc4x4(result, t, l, hasTop, hasLeft);
                break;

            case 3: // Diagonal down-left
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int i = x + y;
                        result[y * 4 + x] = Average3(t[i], t[i + 1], t[i + 2]);
                    }
                break;

            case 4: // Diagonal down-right
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int d = x - y;
                        result[y * 4 + x] = Average3(
                            DiagonalReference(d - 1, t, l, topLeft),
                            DiagonalReference(d, t, l, topLeft),
                            DiagonalReference(d + 1, t, l, topLeft));
                    }
                break;

            case 5: // Vertical-right
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int k = (2 * x) - y;
                        byte value = k < 0
                            ? Average3(VerticalRightReference(k - 1, t, l, topLeft),
                                VerticalRightReference(k, t, l, topLeft),
                                VerticalRightReference(k + 1, t, l, topLeft))
                            : (k & 1) == 0
                                ? Average2(VerticalRightReference((k / 2) - 1, t, l, topLeft),
                                    VerticalRightReference(k / 2, t, l, topLeft))
                                : Average3(VerticalRightReference(((k - 1) / 2) - 1, t, l, topLeft),
                                    VerticalRightReference((k - 1) / 2, t, l, topLeft),
                                    VerticalRightReference(((k - 1) / 2) + 1, t, l, topLeft));
                        result[y * 4 + x] = value;
                    }
                break;

            case 6: // Horizontal-down (transpose of vertical-right)
                byte[] transposedTop = new byte[8];
                byte[] transposedLeft = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    transposedTop[i] = l[i];
                    transposedLeft[i] = t[i];
                }
                Array.Fill(transposedTop, transposedTop[3], 4, 4);
                byte[] transposed = Predict4x4(5, transposedTop, transposedLeft,
                    topLeft, hasLeft, hasTop, hasTopLeft, true, true);
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        result[y * 4 + x] = transposed[x * 4 + y];
                break;

            case 7: // Vertical-left
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int i = x + (y >> 1);
                        result[y * 4 + x] = (y & 1) == 0
                            ? Average2(t[i], t[i + 1])
                            : Average3(t[i], t[i + 1], t[i + 2]);
                    }
                break;

            case 8: // Horizontal-up (transpose of vertical-left)
                byte[] leftAsTop = new byte[8];
                byte[] topAsLeft = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    leftAsTop[i] = l[i];
                    topAsLeft[i] = t[i];
                }
                Array.Fill(leftAsTop, leftAsTop[3], 4, 4);
                byte[] horizontalUpTranspose = Predict4x4(7, leftAsTop,
                    topAsLeft, topLeft, hasLeft, hasTop, hasTopLeft, true, true);
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        result[y * 4 + x] = horizontalUpTranspose[x * 4 + y];
                break;
        }

        return result;
    }

    /// <summary>
    /// Predicts a 16x16 luma block. top and left contain sixteen reference samples.
    /// Mode values are 0=vertical, 1=horizontal, 2=DC and 3=plane.
    /// </summary>
    public static byte[] Predict16x16(
        uint mode,
        IReadOnlyList<byte> top,
        IReadOnlyList<byte> left,
        byte topLeft,
        bool hasTop,
        bool hasLeft)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(left);
        if (top.Count < 16 || left.Count < 16)
            throw new ArgumentException("Intra16x16 prediction requires sixteen top and left samples.");
        if (mode > 3)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == 0 && !hasTop)
            throw new FormatException("Vertical Intra16x16 prediction requires top reference samples.");
        if (mode == 1 && !hasLeft)
            throw new FormatException("Horizontal Intra16x16 prediction requires left reference samples.");
        if (mode == 3 && (!hasTop || !hasLeft))
            throw new FormatException("Plane Intra16x16 prediction requires top and left reference samples.");

        byte[] result = new byte[256];
        if (mode == 0)
        {
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    result[y * 16 + x] = top[x];
            return result;
        }
        if (mode == 1)
        {
            for (int y = 0; y < 16; y++)
                Array.Fill(result, left[y], y * 16, 16);
            return result;
        }
        if (mode == 2)
        {
            int dc = hasTop && hasLeft
                ? (Sum(top, 16) + Sum(left, 16) + 16) >> 5
                : hasTop
                    ? (Sum(top, 16) + 8) >> 4
                    : hasLeft
                        ? (Sum(left, 16) + 8) >> 4
                        : 128;
            Array.Fill(result, (byte)dc);
            return result;
        }

        int horizontal = 0;
        int vertical = 0;
        for (int i = 1; i <= 8; i++)
        {
            byte topBefore = i == 8 ? topLeft : top[7 - i];
            byte leftBefore = i == 8 ? topLeft : left[7 - i];
            horizontal += i * (top[7 + i] - topBefore);
            vertical += i * (left[7 + i] - leftBefore);
        }
        int a = 16 * (top[15] + left[15]);
        int b = (5 * horizontal + 32) >> 6;
        int c = (5 * vertical + 32) >> 6;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int value = (a + b * (x - 7) + c * (y - 7) + 16) >> 5;
                result[y * 16 + x] = Clip(value);
            }
        return result;
    }

    /// <summary>Adds an 8-bit spatial-domain residual block and clips reconstructed samples.</summary>
    public static byte[] AddResidual(
        IReadOnlyList<byte> prediction,
        IReadOnlyList<int> residual)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(residual);
        if (prediction.Count == 0 || prediction.Count != residual.Count)
            throw new ArgumentException("Prediction and residual arrays must have the same non-zero length.");

        byte[] result = new byte[prediction.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = (byte)Math.Clamp(prediction[i] + (long)residual[i], 0, 255);
        return result;
    }

    /// <summary>Adds residuals to high-bit-depth prediction samples and clips to the configured bit depth.</summary>
    public static ushort[] AddResidualHighBitDepth(
        IReadOnlyList<ushort> prediction,
        IReadOnlyList<int> residual,
        int bitDepth)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(residual);
        if (prediction.Count == 0 || prediction.Count != residual.Count)
            throw new ArgumentException("Prediction and residual arrays must have the same non-zero length.");
        if (bitDepth is < 8 or > 14)
            throw new ArgumentOutOfRangeException(nameof(bitDepth));

        ushort[] result = new ushort[prediction.Count];
        int maximum = (1 << bitDepth) - 1;
        for (int i = 0; i < result.Length; i++)
            result[i] = (ushort)Math.Clamp(prediction[i] + (long)residual[i], 0, maximum);
        return result;
    }

    private static void RequireReferences(uint mode, bool hasTop, bool hasLeft, bool hasTopLeft)
    {
        if ((mode is 0 or 3 or 7) && !hasTop)
            throw new FormatException("The selected Intra4x4 mode requires top reference samples.");
        if ((mode is 1 or 8) && !hasLeft)
            throw new FormatException("The selected Intra4x4 mode requires left reference samples.");
        if ((mode is 4 or 5 or 6) && (!hasTop || !hasLeft || !hasTopLeft))
            throw new FormatException("The selected directional Intra4x4 mode requires top, left and top-left samples.");
    }

    private static void FillDc4x4(byte[] output, byte[] top, byte[] left, bool hasTop, bool hasLeft)
    {
        int dc = hasTop && hasLeft
            ? (Sum(top, 4) + Sum(left, 4) + 4) >> 3
            : hasTop
                ? (Sum(top, 4) + 2) >> 2
                : hasLeft
                    ? (Sum(left, 4) + 2) >> 2
                    : 128;
        Array.Fill(output, (byte)dc);
    }

    private static byte[] ExtendTop(IReadOnlyList<byte> source, bool hasTopRight)
    {
        byte[] result = new byte[9];
        for (int i = 0; i < 4; i++)
            result[i] = source[i];
        if (hasTopRight)
        {
            for (int i = 4; i < 9; i++)
                result[i] = i < source.Count ? source[i] : source[^1];
        }
        else
        {
            Array.Fill(result, result[3], 4, 5);
        }
        return result;
    }

    private static byte[] ExtendLeft(IReadOnlyList<byte> source) =>
        [source[0], source[1], source[2], source[3]];

    private static byte VerticalRightReference(int index, byte[] top, byte[] left, byte topLeft) =>
        index >= 0 ? top[Math.Min(index, 7)]
            : index == -1 ? topLeft
            : left[Math.Min(-index - 2, 3)];

    private static byte DiagonalReference(int index, byte[] top, byte[] left, byte topLeft) =>
        index >= 0 ? top[Math.Min(index, 7)]
            : index == -1 ? topLeft
            : left[Math.Min(-index - 2, 3)];

    private static byte Average2(byte a, byte b) => (byte)((a + b + 1) >> 1);
    private static byte Average3(byte a, byte b, byte c) => (byte)((a + 2 * b + c + 2) >> 2);
    private static int Sum(IReadOnlyList<byte> values, int count)
    {
        int sum = 0;
        for (int i = 0; i < count; i++)
            sum += values[i];
        return sum;
    }
    private static byte Clip(int value) => (byte)Math.Clamp(value, 0, 255);
}
