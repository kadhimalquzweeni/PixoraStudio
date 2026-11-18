using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Implements the frame-picture temporal direct distance scaling operation.
/// Selection and storage of the collocated picture/partition are supplied by
/// the slice/DPB integration layer.
/// </summary>
public sealed class H264BTemporalDirectMotionVectorDeriver
{
    public H264TemporalDirectMotionVectorResult Derive(
        H264TemporalDirectPredictionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        H264MotionVector collocated = H264MotionVectorRange.Clip(context.CollocatedMotionVector);

        int td = Clip(context.CollocatedReferencePictureOrderCount -
            context.CollocatedPictureOrderCount, -128, 127);
        int tb = Clip(context.CurrentPictureOrderCount -
            context.CollocatedPictureOrderCount, -128, 127);

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

    private static int Clip(int value, int minimum, int maximum) =>
        Math.Clamp(value, minimum, maximum);
}
