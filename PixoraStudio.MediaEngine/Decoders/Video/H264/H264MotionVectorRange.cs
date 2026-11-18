using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Applies the H.264 motion-vector component range for frame-coded
/// quarter-luma-sample vectors: Clip3(-2^15, 2^15 - 1, mv).
/// </summary>
public static class H264MotionVectorRange
{
    public const int MinimumComponent = -32768;
    public const int MaximumComponent = 32767;

    public static int ClipComponent(int value) =>
        Math.Clamp(value, MinimumComponent, MaximumComponent);

    public static H264MotionVector Clip(H264MotionVector motionVector) =>
        new(
            ClipComponent(motionVector.Horizontal),
            ClipComponent(motionVector.Vertical));
}
