namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Maps H.264 4x4 residual scan positions into raster-order coefficient arrays.</summary>
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

    public static int[] ToRasterOrder(IReadOnlyList<int> scanOrder, bool fieldScan = false)
    {
        ArgumentNullException.ThrowIfNull(scanOrder);
        if (scanOrder.Count != 16)
            throw new ArgumentException("A 4x4 scan requires exactly 16 coefficients.", nameof(scanOrder));

        int[] scan = fieldScan ? FieldScan4X4 : ZigZag4X4;
        int[] raster = new int[16];
        for (int i = 0; i < 16; i++)
            raster[scan[i]] = scanOrder[i];
        return raster;
    }

    public static int[] ToScanOrder(IReadOnlyList<int> rasterOrder, bool fieldScan = false)
    {
        ArgumentNullException.ThrowIfNull(rasterOrder);
        if (rasterOrder.Count != 16)
            throw new ArgumentException("A 4x4 scan requires exactly 16 coefficients.", nameof(rasterOrder));

        int[] scan = fieldScan ? FieldScan4X4 : ZigZag4X4;
        int[] result = new int[16];
        for (int i = 0; i < 16; i++)
            result[i] = rasterOrder[scan[i]];
        return result;
    }
}
