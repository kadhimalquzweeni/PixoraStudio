using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Combines list-0 and list-1 motion-compensated predictions for B slices.
/// Supports default averaging, explicit weighted bi-prediction and implicit
/// weighted bi-prediction for 8-bit 4:2:0/4:2:2 frame pictures.
/// </summary>
public static class H264BiPredictionCombiner
{
    public static H264MotionCompensatedBlock CombineDefault(
        H264MotionCompensatedBlock prediction0,
        H264MotionCompensatedBlock prediction1,
        uint chromaFormatIdc)
    {
        ValidateInputs(prediction0, prediction1, chromaFormatIdc);
        return CombinePlanes(prediction0, prediction1,
            static (a, b) => (a + b + 1) >> 1);
    }

    public static H264MotionCompensatedBlock CombineExplicit(
        H264MotionCompensatedBlock prediction0,
        H264MotionCompensatedBlock prediction1,
        H264PredictionWeightTable table,
        int referenceIndex0,
        int referenceIndex1,
        uint chromaFormatIdc)
    {
        ArgumentNullException.ThrowIfNull(table);
        ValidateInputs(prediction0, prediction1, chromaFormatIdc);
        if (referenceIndex0 < 0 || referenceIndex0 >= table.List0.Count)
            throw new ArgumentOutOfRangeException(nameof(referenceIndex0));
        if (referenceIndex1 < 0 || referenceIndex1 >= table.List1.Count)
            throw new ArgumentOutOfRangeException(nameof(referenceIndex1));
        if (table.LumaLog2WeightDenom > 7 ||
            table.ChromaLog2WeightDenom is null or > 7)
            throw new FormatException("Bi-prediction weight denominators must be in the range 0..7.");

        H264PredictionWeight weight0 = table.List0[referenceIndex0];
        H264PredictionWeight weight1 = table.List1[referenceIndex1];
        ValidateWeight(weight0);
        ValidateWeight(weight1);

        int lumaDenom = (int)table.LumaLog2WeightDenom;
        int chromaDenom = (int)table.ChromaLog2WeightDenom.Value;
        byte[] luma = CombineExplicitPlane(
            prediction0.Luma, prediction1.Luma,
            GetLumaWeight(weight0, lumaDenom), GetLumaWeight(weight1, lumaDenom),
            GetLumaOffset(weight0), GetLumaOffset(weight1), lumaDenom);
        byte[] cb = CombineExplicitPlane(
            prediction0.Cb, prediction1.Cb,
            GetChromaWeight(weight0, 0, chromaDenom),
            GetChromaWeight(weight1, 0, chromaDenom),
            GetChromaOffset(weight0, 0), GetChromaOffset(weight1, 0), chromaDenom);
        byte[] cr = CombineExplicitPlane(
            prediction0.Cr, prediction1.Cr,
            GetChromaWeight(weight0, 1, chromaDenom),
            GetChromaWeight(weight1, 1, chromaDenom),
            GetChromaOffset(weight0, 1), GetChromaOffset(weight1, 1), chromaDenom);

        return new H264MotionCompensatedBlock(
            prediction0.LumaWidth, prediction0.LumaHeight,
            prediction0.ChromaWidth, prediction0.ChromaHeight,
            luma, cb, cr);
    }

    public static H264MotionCompensatedBlock CombineImplicit(
        H264MotionCompensatedBlock prediction0,
        H264MotionCompensatedBlock prediction1,
        int currentPictureOrderCount,
        int referencePictureOrderCount0,
        int referencePictureOrderCount1,
        bool reference0IsLongTerm,
        bool reference1IsLongTerm,
        uint chromaFormatIdc)
    {
        ValidateInputs(prediction0, prediction1, chromaFormatIdc);
        int td = Clip((long)referencePictureOrderCount1 - referencePictureOrderCount0, -128, 127);
        int tb = Clip((long)currentPictureOrderCount - referencePictureOrderCount0, -128, 127);
        int weight1 = 32;
        if (!reference0IsLongTerm && !reference1IsLongTerm && td != 0)
        {
            int tx = (16384 + Math.Abs(td / 2)) / td;
            int distanceScaleFactor = Clip((tb * tx + 32) >> 6, -1024, 1023);
            weight1 = distanceScaleFactor >> 2;
            if (weight1 is < -64 or > 128)
                weight1 = 32;
        }

        int weight0 = 64 - weight1;
        return CombinePlanes(prediction0, prediction1,
            (a, b) => Clip((weight0 * a + weight1 * b + 32) >> 6, 0, 255));
    }

    private static byte[] CombineExplicitPlane(
        IReadOnlyList<byte> plane0,
        IReadOnlyList<byte> plane1,
        int weight0,
        int weight1,
        int offset0,
        int offset1,
        int denominator)
    {
        int rounding = 1 << denominator;
        int offset = (offset0 + offset1 + 1) >> 1;
        byte[] result = new byte[plane0.Count];
        for (int i = 0; i < result.Length; i++)
        {
            int weighted = (weight0 * plane0[i] + weight1 * plane1[i] + rounding)
                >> (denominator + 1);
            result[i] = (byte)Clip(weighted + offset, 0, 255);
        }
        return result;
    }

    private static H264MotionCompensatedBlock CombinePlanes(
        H264MotionCompensatedBlock p0,
        H264MotionCompensatedBlock p1,
        Func<int, int, int> combine)
    {
        byte[] luma = CombinePlane(p0.Luma, p1.Luma, combine);
        byte[] cb = CombinePlane(p0.Cb, p1.Cb, combine);
        byte[] cr = CombinePlane(p0.Cr, p1.Cr, combine);
        return new H264MotionCompensatedBlock(
            p0.LumaWidth, p0.LumaHeight,
            p0.ChromaWidth, p0.ChromaHeight,
            luma, cb, cr);
    }

    private static byte[] CombinePlane(
        IReadOnlyList<byte> p0,
        IReadOnlyList<byte> p1,
        Func<int, int, int> combine)
    {
        byte[] result = new byte[p0.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = (byte)Clip(combine(p0[i], p1[i]), 0, 255);
        return result;
    }

    private static int GetLumaWeight(H264PredictionWeight weight, int denominator) =>
        weight.LumaWeightFlag ? weight.LumaWeight!.Value : 1 << denominator;

    private static int GetLumaOffset(H264PredictionWeight weight) =>
        weight.LumaWeightFlag ? weight.LumaOffset!.Value : 0;

    private static int GetChromaWeight(H264PredictionWeight weight, int component, int denominator) =>
        weight.ChromaWeightFlag ? weight.ChromaWeights[component] : 1 << denominator;

    private static int GetChromaOffset(H264PredictionWeight weight, int component) =>
        weight.ChromaWeightFlag ? weight.ChromaOffsets[component] : 0;

    private static void ValidateWeight(H264PredictionWeight weight)
    {
        ArgumentNullException.ThrowIfNull(weight);
        if (weight.LumaWeightFlag &&
            (weight.LumaWeight is null or < -128 or > 127 ||
             weight.LumaOffset is null or < -128 or > 127))
            throw new FormatException("Explicit luma weights and offsets must be in -128..127.");
        if (weight.ChromaWeightFlag &&
            (weight.ChromaWeights.Count != 2 || weight.ChromaOffsets.Count != 2 ||
             weight.ChromaWeights.Any(value => value is < -128 or > 127) ||
             weight.ChromaOffsets.Any(value => value is < -128 or > 127)))
            throw new FormatException("Explicit chroma weights and offsets must contain two values in -128..127.");
    }

    private static void ValidateInputs(
        H264MotionCompensatedBlock p0,
        H264MotionCompensatedBlock p1,
        uint chromaFormatIdc)
    {
        ArgumentNullException.ThrowIfNull(p0);
        ArgumentNullException.ThrowIfNull(p1);
        if (chromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("B bi-prediction supports 4:2:0 and 4:2:2 only.");
        if (p0.LumaWidth <= 0 || p0.LumaHeight <= 0 ||
            p0.ChromaWidth <= 0 || p0.ChromaHeight <= 0 ||
            p0.Luma.Count != checked(p0.LumaWidth * p0.LumaHeight) ||
            p0.Cb.Count != checked(p0.ChromaWidth * p0.ChromaHeight) ||
            p0.Cr.Count != p0.Cb.Count ||
            p0.LumaWidth != p1.LumaWidth || p0.LumaHeight != p1.LumaHeight ||
            p0.ChromaWidth != p1.ChromaWidth || p0.ChromaHeight != p1.ChromaHeight ||
            p0.Luma.Count != p1.Luma.Count || p0.Cb.Count != p1.Cb.Count ||
            p0.Cr.Count != p1.Cr.Count)
            throw new ArgumentException("List-0 and list-1 prediction planes must have identical, internally consistent geometry.");
    }

    private static int Clip(int value, int minimum, int maximum) =>
        Math.Clamp(value, minimum, maximum);

    private static int Clip(long value, int minimum, int maximum) =>
        (int)Math.Clamp(value, minimum, maximum);
}
