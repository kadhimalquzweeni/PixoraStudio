namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Performs the 50-percent overlap-add stage for consecutive AAC IMDCT blocks.
/// One instance must be retained per decoded channel.
/// </summary>
public sealed class AacSynthesisOverlapAdd
{
    private float[] _previousTail = new float[AacImdct.LongSpectrumLength];

    /// <summary>
    /// Adds the first half of the current windowed IMDCT block to the
    /// previous block's tail and retains the current block's second half.
    /// </summary>
    public float[] Process(ReadOnlySpan<float> windowedBlock)
    {
        if (windowedBlock.Length != AacImdct.LongOutputLength)
        {
            throw new ArgumentException(
                "An AAC overlap-add block must contain 2048 samples.",
                nameof(windowedBlock));
        }

        float[] output = new float[AacImdct.LongSpectrumLength];

        for (int i = 0; i < output.Length; i++)
        {
            output[i] = _previousTail[i] + windowedBlock[i];
            _previousTail[i] = windowedBlock[i + output.Length];
        }

        return output;
    }

    /// <summary>
    /// Clears the saved overlap from the previous frame.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_previousTail);
    }
}
