using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Implements frame-picture temporal direct derivation, including long-term/
/// equal-POC handling, distance scaling and clipping. The caller supplies the
/// collocated partition and its mapped current list-0 reference index.
/// </summary>
public sealed class H264BTemporalDirectMotionVectorDeriver
{
    public H264TemporalDirectMotionVectorResult Derive(
        H264TemporalDirectPredictionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.MappedReferenceIndexL0 < 0)
            throw new FormatException("The collocated reference picture is not present in the current RefPicList0.");
        H264MotionVector collocated = H264MotionVectorRange.Clip(context.CollocatedMotionVector);

        // pic1 is the collocated picture (RefPicList1[0]); pic0 is the
        // collocated partition's mapped reference in the current RefPicList0.
        int td = Clip((long)context.CollocatedPictureOrderCount -
            context.CollocatedReferencePictureOrderCount, -128, 127);
        int tb = Clip((long)context.CurrentPictureOrderCount -
            context.CollocatedReferencePictureOrderCount, -128, 127);

        if (context.CollocatedReferenceIsLongTerm || td == 0)
        {
            return new H264TemporalDirectMotionVectorResult(
                collocated, new H264MotionVector(0, 0), 256, context.MappedReferenceIndexL0);
        }

        int tx = (16384 + Math.Abs(td / 2)) / td;
        int scale = Clip((tb * tx + 32) >> 6, -1024, 1023);
        H264MotionVector list0 = H264MotionVectorRange.Clip(new(
            (scale * collocated.Horizontal + 128) >> 8,
            (scale * collocated.Vertical + 128) >> 8));
        H264MotionVector list1 = H264MotionVectorRange.Clip(new(
            list0.Horizontal - collocated.Horizontal,
            list0.Vertical - collocated.Vertical));

        return new H264TemporalDirectMotionVectorResult(list0, list1, scale, context.MappedReferenceIndexL0);
    }

    private static int Clip(long value, int minimum, int maximum) =>
        (int)Math.Clamp(value, minimum, maximum);
}
