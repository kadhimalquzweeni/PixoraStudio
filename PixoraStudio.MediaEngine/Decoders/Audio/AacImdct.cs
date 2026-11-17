namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Computes the inverse modified discrete cosine transform used by AAC.
/// The implementation favours clarity and deterministic results over a
/// fast factorised transform; a 1024-line AAC-LC block is supported.
/// </summary>
public sealed class AacImdct
{
    public const int LongSpectrumLength = 1024;
    public const int LongOutputLength = 2048;
    public const int ShortSpectrumLength = 128;
    public const int ShortOutputLength = 256;

    /// <summary>
    /// Transforms N spectral coefficients into the corresponding 2N
    /// time-domain IMDCT block, using the conventional 2/N normalisation.
    /// </summary>
    public float[] Transform(ReadOnlySpan<float> coefficients)
    {
        if (coefficients.Length is not (ShortSpectrumLength or LongSpectrumLength))
        {
            throw new ArgumentException(
                "AAC IMDCT input must contain either 128 or 1024 coefficients.",
                nameof(coefficients));
        }

        int n = coefficients.Length;
        int outputLength = checked(n * 2);
        float[] output = new float[outputLength];
        double scale = 2.0 / n;
        double factor = Math.PI / n;

        for (int sample = 0; sample < outputLength; sample++)
        {
            double sum = 0;
            double phase = (sample + 0.5 + (n / 2.0)) * factor;

            for (int coefficient = 0; coefficient < n; coefficient++)
            {
                double angle = phase * (coefficient + 0.5);
                sum += coefficients[coefficient] * Math.Cos(angle);
            }

            output[sample] = (float)(sum * scale);
        }

        return output;
    }
}
