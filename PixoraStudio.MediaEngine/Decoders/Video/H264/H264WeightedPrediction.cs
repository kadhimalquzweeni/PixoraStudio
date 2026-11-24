using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Applies the H.264 uni-prediction weighting process to a motion-compensated
/// list-0 partition. The input block is not mutated; a new block is returned.
/// This implementation is limited to 8-bit frame-coded 4:2:0 and 4:2:2 data.
/// </summary>
public static class H264WeightedPrediction
{
    public static H264MotionCompensatedBlock ApplyList0(
        H264MotionCompensatedBlock prediction,
        H264PredictionWeightTable? table,
        int referenceIndex,
        uint chromaFormatIdc)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        if (referenceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(referenceIndex));
        if (chromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Weighted prediction currently supports 4:2:0 and 4:2:2.");
        ValidateBlock(prediction);

        if (table is null)
            return Clone(prediction);
        if (table.LumaLog2WeightDenom > 7 ||
            table.ChromaLog2WeightDenom is null or > 7)
            throw new FormatException("Prediction weight denominators must be in the range 0..7.");
        if (referenceIndex >= table.List0.Count)
            throw new ArgumentOutOfRangeException(nameof(referenceIndex),
                "The selected reference has no list-0 prediction weight entry.");

        H264PredictionWeight weight = table.List0[referenceIndex];
        ValidateWeight(weight);

        int lumaDenom = checked((int)table.LumaLog2WeightDenom);
        int chromaDenom = checked((int)table.ChromaLog2WeightDenom.Value);
        int defaultLumaWeight = 1 << lumaDenom;
        int defaultChromaWeight = 1 << chromaDenom;

        byte[] luma = ApplyPlane(prediction.Luma,
            weight.LumaWeightFlag ? weight.LumaWeight!.Value : defaultLumaWeight,
            weight.LumaWeightFlag ? weight.LumaOffset!.Value : 0,
            lumaDenom);

        int cbWeight = weight.ChromaWeightFlag ? weight.ChromaWeights[0] : defaultChromaWeight;
        int crWeight = weight.ChromaWeightFlag ? weight.ChromaWeights[1] : defaultChromaWeight;
        int cbOffset = weight.ChromaWeightFlag ? weight.ChromaOffsets[0] : 0;
        int crOffset = weight.ChromaWeightFlag ? weight.ChromaOffsets[1] : 0;

        byte[] cb = ApplyPlane(prediction.Cb, cbWeight, cbOffset, chromaDenom);
        byte[] cr = ApplyPlane(prediction.Cr, crWeight, crOffset, chromaDenom);
        return new H264MotionCompensatedBlock(
            prediction.LumaWidth, prediction.LumaHeight,
            prediction.ChromaWidth, prediction.ChromaHeight,
            luma, cb, cr);
    }

    private static byte[] ApplyPlane(
        IReadOnlyList<byte> samples, int weight, int offset, int denominator)
    {
        int rounding = denominator == 0 ? 0 : 1 << (denominator - 1);
        byte[] result = new byte[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            int scaled = denominator == 0
                ? checked(weight * samples[i])
                : checked((weight * samples[i] + rounding) >> denominator);
            result[i] = (byte)Math.Clamp(checked(scaled + offset), 0, 255);
        }
        return result;
    }

    private static void ValidateWeight(H264PredictionWeight weight)
    {
        ArgumentNullException.ThrowIfNull(weight);
        if (weight.LumaWeightFlag &&
            (weight.LumaWeight is null or < -128 or > 127 ||
             weight.LumaOffset is null or < -128 or > 127))
            throw new FormatException("Explicit luma prediction weights and offsets must be in -128..127.");
        if (weight.ChromaWeightFlag &&
            (weight.ChromaWeights.Count != 2 || weight.ChromaOffsets.Count != 2 ||
             weight.ChromaWeights.Any(v => v is < -128 or > 127) ||
             weight.ChromaOffsets.Any(v => v is < -128 or > 127)))
            throw new FormatException("Explicit Cb/Cr prediction weights and offsets must each contain two values in -128..127.");
    }

    private static void ValidateBlock(H264MotionCompensatedBlock block)
    {
        if (block.LumaWidth <= 0 || block.LumaHeight <= 0 ||
            block.ChromaWidth <= 0 || block.ChromaHeight <= 0 ||
            block.Luma.Length != checked(block.LumaWidth * block.LumaHeight) ||
            block.Cb.Length != checked(block.ChromaWidth * block.ChromaHeight) ||
            block.Cr.Length != block.Cb.Length)
            throw new ArgumentException("The motion-compensated block has inconsistent plane dimensions.", nameof(block));
    }

    private static H264MotionCompensatedBlock Clone(H264MotionCompensatedBlock block) =>
        new(block.LumaWidth, block.LumaHeight, block.ChromaWidth, block.ChromaHeight,
            (byte[])block.Luma.Clone(), (byte[])block.Cb.Clone(), (byte[])block.Cr.Clone());
}
