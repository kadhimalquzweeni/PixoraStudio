using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// H.264 4x4 inverse quantisation and luma quantiser derivation.
/// Scaling lists supplied to Dequantize4x4 must already be resolved and in raster order.
/// </summary>
public static class H264InverseQuantizer
{
    private static readonly int[,] LevelScale =
    {
        { 10, 11, 13, 14, 16, 18 },
        { 16, 18, 20, 23, 25, 29 },
        { 13, 14, 16, 18, 20, 23 }
    };

    /// <summary>Derives SliceQPY. The returned value may be negative for high-bit-depth streams.</summary>
    public static int CalculateLumaQpY(
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        H264SliceHeader slice)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        ArgumentNullException.ThrowIfNull(slice);
        if (sps.BitDepthLumaMinus8 > 6)
            throw new FormatException("H.264 luma bit depth exceeds the supported 14-bit range.");

        int qpBdOffsetY = checked(6 * (int)sps.BitDepthLumaMinus8);
        int qpY = checked(26 + pps.PicInitQpMinus26 + slice.SliceQpDelta);
        if (qpY < -qpBdOffsetY || qpY > 51)
            throw new FormatException("Derived SliceQPY is outside the range permitted by the SPS bit depth.");
        return qpY;
    }

    /// <summary>Converts SliceQPY to QPY-prime by applying the luma bit-depth offset.</summary>
    public static int ToQpPrimeY(H264SequenceParameterSet sps, int qpY)
    {
        ArgumentNullException.ThrowIfNull(sps);
        if (sps.BitDepthLumaMinus8 > 6)
            throw new FormatException("H.264 luma bit depth exceeds the supported 14-bit range.");

        int qpBdOffsetY = checked(6 * (int)sps.BitDepthLumaMinus8);
        if (qpY < -qpBdOffsetY || qpY > 51)
            throw new ArgumentOutOfRangeException(nameof(qpY), "SliceQPY is outside the range permitted by the SPS bit depth.");
        return checked(qpY + qpBdOffsetY);
    }

    /// <summary>
    /// Applies H.264 4x4 inverse scaling using the effective scaling matrix resolved from SPS/PPS.
    /// scalingListIndex is 0-2 for intra lists and 3-5 for inter lists.
    /// </summary>
    public static int[] Dequantize4x4(
        IReadOnlyList<int> coefficients,
        int qpPrimeY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        int scalingListIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        if (scalingListIndex is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(scalingListIndex));

        int[][] scalingMatrices = H264ScalingMatrixResolver.Resolve4x4(sps, pps);
        return Dequantize4x4(coefficients, qpPrimeY, scalingMatrices[scalingListIndex]);
    }

    /// <summary>
    /// Applies H.264 4x4 inverse scaling. qpPrimeY is QPY-prime (SliceQPY + QpBdOffsetY).
    /// scalingListRasterOrder is the effective, already-resolved 16-entry matrix in raster order.
    /// </summary>
    public static int[] Dequantize4x4(
        IReadOnlyList<int> coefficients,
        int qpPrimeY,
        IReadOnlyList<int> scalingListRasterOrder)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        ArgumentNullException.ThrowIfNull(scalingListRasterOrder);
        if (coefficients.Count != 16)
            throw new ArgumentException("A 4x4 inverse quantisation block requires exactly 16 coefficients.", nameof(coefficients));
        if (scalingListRasterOrder.Count != 16)
            throw new ArgumentException("A 4x4 scaling list requires exactly 16 raster-order values.", nameof(scalingListRasterOrder));
        if (qpPrimeY is < 0 or > 87)
            throw new ArgumentOutOfRangeException(nameof(qpPrimeY), "QPY-prime must be between 0 and 87 for supported H.264 bit depths.");

        for (int i = 0; i < 16; i++)
            if (scalingListRasterOrder[i] is < 1 or > 255)
                throw new ArgumentOutOfRangeException(nameof(scalingListRasterOrder), "Scaling-list entries must be between 1 and 255.");

        int qpDiv6 = qpPrimeY / 6;
        int qpMod6 = qpPrimeY % 6;
        int[] result = new int[16];

        for (int index = 0; index < 16; index++)
        {
            int row = index / 4;
            int column = index % 4;
            int positionClass = ((row & 1) == 0, (column & 1) == 0) switch
            {
                (true, true) => 0,
                (false, false) => 2,
                _ => 1
            };

            long levelScale = (long)LevelScale[positionClass, qpMod6] * scalingListRasterOrder[index];
            long product = checked((long)coefficients[index] * levelScale);
            long value = qpDiv6 >= 4
                ? checked(product << (qpDiv6 - 4))
                : (product + (1L << (3 - qpDiv6))) >> (4 - qpDiv6);

            if (value is > int.MaxValue or < int.MinValue)
                throw new FormatException("H.264 inverse-quantised coefficient exceeds Int32.");
            result[index] = (int)value;
        }

        return result;
    }
}
