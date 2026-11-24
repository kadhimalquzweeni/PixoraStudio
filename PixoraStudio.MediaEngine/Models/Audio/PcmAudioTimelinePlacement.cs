namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Describes how a decoded PCM block is placed on a continuous presentation
/// timeline, including leading silence and samples already covered by earlier
/// blocks.
/// </summary>
public readonly record struct PcmAudioTimelinePlacementResult(
    long SilenceFrames,
    int FramesToSkip,
    int FramesToWrite,
    long ResultingWrittenFrames);

/// <summary>
/// Calculates the placement of decoded audio blocks on a sample-frame timeline.
/// </summary>
public static class PcmAudioTimelinePlacement
{
    public static PcmAudioTimelinePlacementResult Calculate(
        long presentationStartFrame,
        long writtenFrames,
        int decodedFrameCount)
    {
        if (writtenFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(writtenFrames));

        if (decodedFrameCount < 0)
            throw new ArgumentOutOfRangeException(nameof(decodedFrameCount));

        if (presentationStartFrame == long.MinValue)
            throw new ArgumentOutOfRangeException(nameof(presentationStartFrame));

        long startFrame = presentationStartFrame;
        int framesToSkip = 0;

        if (startFrame < 0)
        {
            framesToSkip = (int)Math.Min(decodedFrameCount, -startFrame);
            startFrame = 0;
        }

        long silenceFrames = startFrame > writtenFrames
            ? startFrame - writtenFrames
            : 0;

        if (startFrame < writtenFrames)
        {
            int remainingFrames = decodedFrameCount - framesToSkip;
            long fullOverlapThreshold = writtenFrames - remainingFrames;

            if (startFrame <= fullOverlapThreshold)
            {
                framesToSkip = decodedFrameCount;
            }
            else
            {
                // This subtraction is now bounded by remainingFrames and
                // therefore cannot overflow even for extreme timeline values.
                framesToSkip += checked((int)(writtenFrames - startFrame));
            }
        }

        int framesToWrite = decodedFrameCount - framesToSkip;
        long resultingWrittenFrames = checked(
            writtenFrames + silenceFrames + framesToWrite);

        return new PcmAudioTimelinePlacementResult(
            silenceFrames,
            framesToSkip,
            framesToWrite,
            resultingWrittenFrames);
    }
}
