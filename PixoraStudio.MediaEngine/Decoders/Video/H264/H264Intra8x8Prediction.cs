namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// H.264 Intra_8x8 luma prediction. The caller supplies the sixteen top
/// samples (including the top-right extension), eight left samples and
/// explicit availability flags. Reference filtering is performed here.
/// </summary>
public static class H264Intra8x8Prediction
{
    public static byte[] Predict(
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
        if (mode > 8)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (top.Count < 16 || left.Count < 8)
            throw new ArgumentException("Intra8x8 prediction requires sixteen top and eight left samples.");
        if ((mode is 0 or 3 or 7) && !hasTop)
            throw new FormatException("The selected Intra8x8 mode requires top reference samples.");
        if ((mode is 1 or 8) && !hasLeft)
            throw new FormatException("The selected Intra8x8 mode requires left reference samples.");
        if ((mode is 4 or 5 or 6) && (!hasTop || !hasLeft || !hasTopLeft))
            throw new FormatException("The selected directional Intra8x8 mode requires top, left and top-left samples.");

        byte[] rawTop = top.Take(16).ToArray();
        if (!hasTopRight)
            Array.Fill(rawTop, rawTop[7], 8, 8);

        // DC uses unfiltered edge samples. Directional and vertical/horizontal
        // modes use the normative three-tap filtered reference array.
        byte filterTopLeft = hasTopLeft ? topLeft
            : hasTop ? rawTop[0]
            : hasLeft ? left[0]
            : (byte)128;
        byte[] filteredTop = FilterTop(rawTop, filterTopLeft);
        byte[] filteredLeft = FilterLeft(left, filterTopLeft);
        byte filteredTopLeft = Average3(left[0], filterTopLeft, rawTop[0]);

        IReadOnlyList<byte> t = mode == 2 ? rawTop : filteredTop;
        IReadOnlyList<byte> l = mode == 2 ? left : filteredLeft;
        byte tl = mode == 2 ? topLeft : filteredTopLeft;
        byte[] result = new byte[64];

        switch (mode)
        {
            case 0: // Vertical
                for (int y = 0; y < 8; y++)
                    Array.Copy(t.ToArray(), 0, result, y * 8, 8);
                break;
            case 1: // Horizontal
                for (int y = 0; y < 8; y++)
                    Array.Fill(result, l[y], y * 8, 8);
                break;
            case 2: // DC
                FillDc(result, rawTop, left, hasTop, hasLeft);
                break;
            case 3: // Diagonal down-left
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                        result[y * 8 + x] = Average3(
                            t[x + y], t[Math.Min(x + y + 1, 15)],
                            t[Math.Min(x + y + 2, 15)]);
                break;
            case 4: // Diagonal down-right
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        int d = x - y;
                        result[y * 8 + x] = Average3(
                            DiagonalReference(d - 1, t, l, tl),
                            DiagonalReference(d, t, l, tl),
                            DiagonalReference(d + 1, t, l, tl));
                    }
                break;
            case 5: // Vertical-right
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        int k = 2 * x - y;
                        result[y * 8 + x] = k < 0
                            ? Average3(VerticalRightReference(k - 1, t, l, tl),
                                VerticalRightReference(k, t, l, tl),
                                VerticalRightReference(k + 1, t, l, tl))
                            : (k & 1) == 0
                                ? Average2(VerticalRightReference(k / 2 - 1, t, l, tl),
                                    VerticalRightReference(k / 2, t, l, tl))
                                : Average3(VerticalRightReference((k - 1) / 2 - 1, t, l, tl),
                                    VerticalRightReference((k - 1) / 2, t, l, tl),
                                    VerticalRightReference((k - 1) / 2 + 1, t, l, tl));
                    }
                break;
            case 6: // Horizontal-down (transpose of vertical-right)
                byte[] transposedTop = new byte[16];
                byte[] transposedLeft = new byte[8];
                for (int i = 0; i < 8; i++)
                {
                    transposedTop[i] = l[i];
                    transposedLeft[i] = t[i];
                }
                Array.Fill(transposedTop, transposedTop[7], 8, 8);
                byte[] verticalRight = PredictDirectional5(transposedTop,
                    transposedLeft, tl);
                Transpose8(verticalRight, result);
                break;
            case 7: // Vertical-left
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        int i = x + (y >> 1);
                        result[y * 8 + x] = (y & 1) == 0
                            ? Average2(t[i], t[Math.Min(i + 1, 15)])
                            : Average3(t[i], t[Math.Min(i + 1, 15)],
                                t[Math.Min(i + 2, 15)]);
                    }
                break;
            case 8: // Horizontal-up (transpose of vertical-left)
                byte[] leftAsTop = new byte[16];
                byte[] topAsLeft = new byte[8];
                for (int i = 0; i < 8; i++)
                {
                    leftAsTop[i] = l[i];
                    topAsLeft[i] = t[i];
                }
                Array.Fill(leftAsTop, leftAsTop[7], 8, 8);
                byte[] verticalLeft = PredictDirectional7(leftAsTop);
                Transpose8(verticalLeft, result);
                break;
        }

        return result;
    }

    private static byte[] FilterTop(IReadOnlyList<byte> top, byte topLeft)
    {
        byte[] result = new byte[16];
        result[0] = Average3(topLeft, top[0], top[1]);
        for (int i = 1; i < 15; i++)
            result[i] = Average3(top[i - 1], top[i], top[i + 1]);
        result[15] = (byte)((top[14] + 3 * top[15] + 2) >> 2);
        return result;
    }

    private static byte[] FilterLeft(IReadOnlyList<byte> left, byte topLeft)
    {
        byte[] result = new byte[8];
        result[0] = Average3(topLeft, left[0], left[1]);
        for (int i = 1; i < 7; i++)
            result[i] = Average3(left[i - 1], left[i], left[i + 1]);
        result[7] = (byte)((left[6] + 3 * left[7] + 2) >> 2);
        return result;
    }

    private static void FillDc(byte[] output, IReadOnlyList<byte> top,
        IReadOnlyList<byte> left, bool hasTop, bool hasLeft)
    {
        int sumTop = 0, sumLeft = 0;
        if (hasTop)
            for (int i = 0; i < 8; i++) sumTop += top[i];
        if (hasLeft)
            for (int i = 0; i < 8; i++) sumLeft += left[i];
        int dc = hasTop && hasLeft ? (sumTop + sumLeft + 8) >> 4
            : hasTop ? (sumTop + 4) >> 3
            : hasLeft ? (sumLeft + 4) >> 3 : 128;
        Array.Fill(output, (byte)dc);
    }

    private static byte[] PredictDirectional5(
        IReadOnlyList<byte> top, IReadOnlyList<byte> left, byte topLeft)
    {
        byte[] result = new byte[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int k = 2 * x - y;
                result[y * 8 + x] = k < 0
                    ? Average3(VerticalRightReference(k - 1, top, left, topLeft),
                        VerticalRightReference(k, top, left, topLeft),
                        VerticalRightReference(k + 1, top, left, topLeft))
                    : (k & 1) == 0
                        ? Average2(VerticalRightReference(k / 2 - 1, top, left, topLeft),
                            VerticalRightReference(k / 2, top, left, topLeft))
                        : Average3(VerticalRightReference((k - 1) / 2 - 1, top, left, topLeft),
                            VerticalRightReference((k - 1) / 2, top, left, topLeft),
                            VerticalRightReference((k - 1) / 2 + 1, top, left, topLeft));
            }
        return result;
    }

    private static byte[] PredictDirectional7(IReadOnlyList<byte> top)
    {
        byte[] result = new byte[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int i = x + (y >> 1);
                result[y * 8 + x] = (y & 1) == 0
                    ? Average2(top[i], top[Math.Min(i + 1, 15)])
                    : Average3(top[i], top[Math.Min(i + 1, 15)],
                        top[Math.Min(i + 2, 15)]);
            }
        return result;
    }

    private static void Transpose8(IReadOnlyList<byte> source, byte[] destination)
    {
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                destination[y * 8 + x] = source[x * 8 + y];
    }

    private static byte VerticalRightReference(int index,
        IReadOnlyList<byte> top, IReadOnlyList<byte> left, byte topLeft) =>
        index >= 0 ? top[Math.Min(index, 15)]
            : index == -1 ? topLeft
            : left[Math.Min(-index - 2, 7)];

    private static byte DiagonalReference(int index,
        IReadOnlyList<byte> top, IReadOnlyList<byte> left, byte topLeft) =>
        VerticalRightReference(index, top, left, topLeft);

    private static byte Average2(byte a, byte b) => (byte)((a + b + 1) >> 1);
    private static byte Average3(byte a, byte b, byte c) =>
        (byte)((a + 2 * b + c + 2) >> 2);
}
