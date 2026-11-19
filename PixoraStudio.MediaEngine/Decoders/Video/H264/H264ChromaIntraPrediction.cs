namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Implements the H.264 chroma intra prediction process for 4:2:0 and 4:2:2
/// macroblocks. Samples are supplied in raster order and reference availability
/// must already account for picture, slice and constrained-intra boundaries.
/// </summary>
public static class H264ChromaIntraPrediction
{
    /// <summary>
    /// Predicts one chroma component. Modes are 0=DC, 1=horizontal,
    /// 2=vertical and 3=plane. ChromaFormatIdc 1 produces 8x8;
    /// ChromaFormatIdc 2 produces 8x16.
    /// </summary>
    public static byte[] Predict(
        uint mode,
        uint chromaFormatIdc,
        IReadOnlyList<byte> top,
        IReadOnlyList<byte> left,
        byte topLeft,
        bool hasTop,
        bool hasLeft,
        bool hasTopLeft)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(left);

        (int width, int height) = chromaFormatIdc switch
        {
            1 => (8, 8),
            2 => (8, 16),
            _ => throw new NotSupportedException(
                "Chroma intra prediction supports only 4:2:0 and 4:2:2 chroma formats.")
        };

        if (mode > 3)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (top.Count < width)
            throw new ArgumentException($"Chroma prediction requires at least {width} top samples.", nameof(top));
        if (left.Count < height)
            throw new ArgumentException($"Chroma prediction requires at least {height} left samples.", nameof(left));

        if (mode == 1 && !hasLeft)
            throw new FormatException("Horizontal chroma prediction requires left reference samples.");
        if (mode == 2 && !hasTop)
            throw new FormatException("Vertical chroma prediction requires top reference samples.");
        if (mode == 3 && (!hasTop || !hasLeft || !hasTopLeft))
            throw new FormatException("Plane chroma prediction requires top, left and top-left reference samples.");

        byte[] result = new byte[width * height];
        switch (mode)
        {
            case 0:
                PredictDc(result, width, height, top, left, hasTop, hasLeft);
                break;
            case 1:
                for (int y = 0; y < height; y++)
                    Array.Fill(result, left[y], y * width, width);
                break;
            case 2:
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        result[y * width + x] = top[x];
                break;
            case 3:
                PredictPlane(result, width, height, top, left, topLeft);
                break;
        }

        return result;
    }

    private static void PredictDc(
        byte[] output,
        int width,
        int height,
        IReadOnlyList<byte> top,
        IReadOnlyList<byte> left,
        bool hasTop,
        bool hasLeft)
    {
        // Chroma DC is derived independently for each 4x4 prediction region.
        // With both edges available, top-left and interior-right regions
        // combine their corresponding top and left segments; top-right uses
        // top only and the remaining left-column regions use left only.
        // With a single available edge, its corresponding segment supplies
        // every 4x4 region in that column (top) or row (left).
        int blockColumns = width / 4;
        int blockRows = height / 4;

        for (int blockY = 0; blockY < blockRows; blockY++)
        {
            for (int blockX = 0; blockX < blockColumns; blockX++)
            {
                int value;
                if (hasTop && hasLeft)
                {
                    bool useBothEdges = (blockX == 0 && blockY == 0) ||
                        (blockX > 0 && blockY > 0);
                    if (useBothEdges)
                    {
                        int topSum = Sum4(top, blockX * 4);
                        int leftSum = Sum4(left, blockY * 4);
                        value = (topSum + leftSum + 4) >> 3;
                    }
                    else if (blockY == 0)
                    {
                        value = (Sum4(top, blockX * 4) + 2) >> 2;
                    }
                    else
                    {
                        value = (Sum4(left, blockY * 4) + 2) >> 2;
                    }
                }
                else if (hasTop)
                {
                    value = (Sum4(top, blockX * 4) + 2) >> 2;
                }
                else if (hasLeft)
                {
                    value = (Sum4(left, blockY * 4) + 2) >> 2;
                }
                else
                {
                    value = 128;
                }

                for (int y = blockY * 4; y < (blockY + 1) * 4; y++)
                    Array.Fill(output, (byte)value, y * width + blockX * 4, 4);
            }
        }
    }

    private static void PredictPlane(
        byte[] output,
        int width,
        int height,
        IReadOnlyList<byte> top,
        IReadOnlyList<byte> left,
        byte topLeft)
    {
        int horizontal = 0;
        int vertical = 0;
        int halfWidth = width / 2;
        int halfHeight = height / 2;

        for (int i = 1; i <= halfWidth; i++)
        {
            int before = i == halfWidth ? topLeft : top[halfWidth - 1 - i];
            horizontal += i * (top[halfWidth - 1 + i] - before);
        }

        for (int i = 1; i <= halfHeight; i++)
        {
            int before = i == halfHeight ? topLeft : left[halfHeight - 1 - i];
            vertical += i * (left[halfHeight - 1 + i] - before);
        }

        int b = (17 * horizontal + 16) >> 5;
        int c = height == 8
            ? (17 * vertical + 16) >> 5
            : (5 * vertical + 32) >> 6;
        int a = 16 * (top[width - 1] + left[height - 1]);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int value = (a + b * (x - (halfWidth - 1)) +
                    c * (y - (halfHeight - 1)) + 16) >> 5;
                output[y * width + x] = (byte)Math.Clamp(value, 0, 255);
            }
        }
    }

    private static int Sum4(IReadOnlyList<byte> samples, int start) =>
        samples[start] + samples[start + 1] + samples[start + 2] + samples[start + 3];
}
