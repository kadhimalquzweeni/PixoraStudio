namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Computes the inverse modified discrete cosine transform used by AAC.
/// Cosine kernels are precomputed once per supported block size.
/// </summary>
public sealed class AacImdct
{
    public const int LongSpectrumLength = 1024;
    public const int LongOutputLength = 2048;
    public const int ShortSpectrumLength = 128;
    public const int ShortOutputLength = 256;

    private static readonly Lazy<float[]> LongCosineTable =
        new(() => CreateCosineTable(LongSpectrumLength),
            LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<float[]> ShortCosineTable =
        new(() => CreateCosineTable(ShortSpectrumLength),
            LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Transforms N spectral coefficients into the corresponding 2N
    /// time-domain IMDCT block, using the conventional 2/N normalisation.
    /// </summary>
    public float[] Transform(ReadOnlySpan<float> coefficients)
    {
        int n = coefficients.Length;
        float[] cosineTable = n switch
        {
            ShortSpectrumLength => ShortCosineTable.Value,
            LongSpectrumLength => LongCosineTable.Value,
            _ => throw new ArgumentException(
                "AAC IMDCT input must contain either 128 or 1024 coefficients.",
                nameof(coefficients))
        };

        int outputLength = checked(n * 2);
        float[] output = new float[outputLength];
        double scale = 2.0 / n;

        for (int sample = 0; sample < outputLength; sample++)
        {
            double sum = 0;
            int rowOffset = sample * n;

            for (int coefficient = 0; coefficient < n; coefficient++)
                sum += coefficients[coefficient] * cosineTable[rowOffset + coefficient];

            output[sample] = (float)(sum * scale);
        }

        return output;
    }

    private static float[] CreateCosineTable(int n)
    {
        int outputLength = checked(n * 2);
        float[] table = new float[checked(outputLength * n)];
        double factor = Math.PI / n;

        for (int sample = 0; sample < outputLength; sample++)
        {
            double phase = (sample + 0.5 + (n / 2.0)) * factor;
            int rowOffset = sample * n;

            for (int coefficient = 0; coefficient < n; coefficient++)
            {
                table[rowOffset + coefficient] = (float)Math.Cos(
                    phase * (coefficient + 0.5));
            }
        }

        return table;
    }
}
