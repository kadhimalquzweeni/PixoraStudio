using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reconstructs one 8x8 luma transform block by mapping forward-scan
/// coefficients to raster order, inverse quantising, applying the H.264
/// 8x8 integer inverse transform and adding the residual to prediction.
/// </summary>
public static class H264Intra8x8ResidualReconstructor
{
    public static byte[] Reconstruct(
        IReadOnlyList<byte> prediction,
        IReadOnlyList<int> coefficientsInScanOrder,
        int qpPrimeY,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps,
        int scalingListIndex = 6,
        bool fieldScan = false)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(coefficientsInScanOrder);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (prediction.Count != 64)
            throw new ArgumentException("An 8x8 prediction block must contain exactly 64 samples.", nameof(prediction));
        if (coefficientsInScanOrder.Count != 64)
            throw new ArgumentException("An 8x8 residual block must contain exactly 64 coefficients.", nameof(coefficientsInScanOrder));
        if (!pps.Transform8X8ModeFlag)
            throw new FormatException("The PPS does not enable 8x8 transform processing.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (sps.SeparateColourPlaneFlag || sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("8x8 luma residual reconstruction supports non-separate 4:2:0 and 4:2:2 pictures only.");
        if (sps.BitDepthLumaMinus8 != 0)
            throw new NotSupportedException("8x8 luma residual reconstruction currently supports 8-bit samples only.");

        int[] rasterCoefficients = H264CoefficientScanner.ToRasterOrder(
            coefficientsInScanOrder, fieldScan);
        int[] dequantized = H264InverseQuantizer.Dequantize8x8(
            rasterCoefficients, qpPrimeY, sps, pps, scalingListIndex);
        int[] spatialResidual = H264InverseTransform.Inverse8x8(dequantized);
        return H264IntraPrediction.AddResidual(prediction, spatialResidual);
    }
}
