namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Performs AAC inverse quantisation of spectral coefficients.
/// </summary>
public sealed class AacInverseQuantizer
{
    /// <summary>
    /// Converts one quantised coefficient into its
    /// unscaled inverse-quantised value.
    /// </summary>
    public float Dequantize(int coefficient)
    {
        if (coefficient == 0)
        {
            return 0f;
        }

        double magnitude = Math.Abs((double)coefficient);

        double value = Math.Pow(magnitude, 4.0 / 3.0);

        return (float)Math.CopySign(value, coefficient);
    }

    /// <summary>
    /// Converts a collection of quantised coefficients.
    /// </summary>
    public void Dequantize(
        ReadOnlySpan<int> input,
        Span<float> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException(
                "The output buffer is too small.",
                nameof(output));
        }

        for (int i = 0; i < input.Length; i++)
        {
            output[i] = Dequantize(input[i]);
        }
    }
}