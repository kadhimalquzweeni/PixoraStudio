namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Maps H.264 residual scan positions into raster-order coefficient arrays.</summary>
public static class H264CoefficientScanner
{
    private static readonly int[] ZigZag4X4 =
    [
        0, 1, 4, 8,
        5, 2, 3, 6,
        9, 12, 13, 10,
        7, 11, 14, 15
    ];

    private static readonly int[] FieldScan4X4 =
    [
        0, 4, 1, 8,
        12, 5, 9, 13,
        2, 6, 10, 14,
        3, 7, 11, 15
    ];

    // Table 8-14, ITU-T H.264: inverse 8x8 field scan, expressed
    // as raster indices (row * 8 + column).
    private static readonly int[] FieldScan8X8 =
    [
        0, 8, 16, 1, 9, 24, 32, 17,
        2, 25, 40, 48, 56, 33, 10, 3,
        18, 41, 49, 57, 26, 11, 4, 19,
        34, 42, 50, 58, 27, 12, 5, 20,
        35, 43, 51, 59, 28, 13, 6, 21,
        36, 44, 52, 60, 29, 14, 22, 37,
        45, 53, 61, 30, 7, 15, 38, 46,
        54, 62, 23, 31, 39, 47, 55, 63
    ];

    private static readonly int[] ZigZag8X8 = CreateZigZag8X8();

    public static int[] ToRasterOrder(IReadOnlyList<int> scanOrder, bool fieldScan = false)
    {
        ArgumentNullException.ThrowIfNull(scanOrder);
        int[] scan = GetScan(scanOrder.Count, fieldScan);
        int[] raster = new int[scan.Length];
        for (int i = 0; i < scan.Length; i++)
            raster[scan[i]] = scanOrder[i];
        return raster;
    }

    public static int[] ToScanOrder(IReadOnlyList<int> rasterOrder, bool fieldScan = false)
    {
        ArgumentNullException.ThrowIfNull(rasterOrder);
        int[] scan = GetScan(rasterOrder.Count, fieldScan);
        int[] result = new int[scan.Length];
        for (int i = 0; i < scan.Length; i++)
            result[i] = rasterOrder[scan[i]];
        return result;
    }

    private static int[] GetScan(int count, bool fieldScan) => count switch
    {
        16 => fieldScan ? FieldScan4X4 : ZigZag4X4,
        64 => fieldScan ? FieldScan8X8 : ZigZag8X8,
        _ => throw new ArgumentException("A coefficient scan requires exactly 16 or 64 values.")
    };

    private static int[] CreateZigZag8X8()
    {
        int[] result = new int[64];
        int index = 0;

        for (int diagonal = 0; diagonal <= 14; diagonal++)
        {
            int minimumRow = Math.Max(0, diagonal - 7);
            int maximumRow = Math.Min(7, diagonal);
            if ((diagonal & 1) == 0)
            {
                for (int row = maximumRow; row >= minimumRow; row--)
                {
                    int column = diagonal - row;
                    result[index++] = row * 8 + column;
                }
            }
            else
            {
                for (int row = minimumRow; row <= maximumRow; row++)
                {
                    int column = diagonal - row;
                    result[index++] = row * 8 + column;
                }
            }
        }

        return result;
    }
}
