using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Implements frame-picture temporal direct derivation, including colZeroFlag,
/// long-term/equal-POC handling, distance scaling and clipping. Selection and
/// storage of the collocated picture/partition are supplied by the caller.
/// </summary>
public sealed class H264BTemporalDirectMotionVectorDeriver
{
    public H264TemporalDirectMotionVectorResult Derive(
        H264TemporalDirectPredictionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        H264MotionVector collocated = H264MotionVectorRange.Clip(context.CollocatedMotionVector);

        int td = Clip((long)context.CollocatedReferencePictureOrderCount -
            context.CollocatedPictureOrderCount, -128, 127);
        int tb = Clip((long)context.CurrentPictureOrderCount -
            context.CollocatedPictureOrderCount, -128, 127);

        bool colZeroFlag = context.CollocatedPictureIsShortTerm &&
            context.CollocatedReferenceIndex == 0 &&
            collocated.Horizontal is >= -1 and <= 1 &&
            collocated.Vertical is >= -1 and <= 1;
        if (colZeroFlag)
        {
            return new H264TemporalDirectMotionVectorResult(
                new H264MotionVector(0, 0), new H264MotionVector(0, 0), 256);
        }

        if (context.CollocatedReferenceIsLongTerm || td == 0)
        {
            return new H264TemporalDirectMotionVectorResult(
                collocated, new H264MotionVector(0, 0), 256);
        }

        int tx = (16384 + Math.Abs(td / 2)) / td;
        int scale = Clip((tb * tx + 32) >> 6, -1024, 1023);
        H264MotionVector list0 = H264MotionVectorRange.Clip(new(
            (scale * collocated.Horizontal + 128) >> 8,
            (scale * collocated.Vertical + 128) >> 8));
        H264MotionVector list1 = H264MotionVectorRange.Clip(new(
            list0.Horizontal - collocated.Horizontal,
            list0.Vertical - collocated.Vertical));

        return new H264TemporalDirectMotionVectorResult(list0, list1, scale);
    }

    private static int Clip(long value, int minimum, int maximum) =>
        (int)Math.Clamp(value, minimum, maximum);
}
