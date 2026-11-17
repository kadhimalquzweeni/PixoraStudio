using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>Converts quantised AAC TNS reflection coefficients to LPC coefficients.</summary>
public sealed class AacTnsCoefficientDecoder
{
    // Table 4.49: reflection-coefficient quantiser reconstruction values.
    // Rows are selected by (coef_compress * 2 + coef_res).
    private static readonly float[][] ReflectionCoefficientTables =
    [
        [0f, -0.43388373f, -0.78183150f, -0.97492790f,
         0.98480773f, 0.86602539f, 0.64278758f, 0.34202015f],
        [0f, -0.20791170f, -0.40673664f, -0.58778524f,
         -0.74314481f, -0.86602539f, -0.95105654f, -0.99452192f,
         0.99573416f, 0.96182561f, 0.89516330f, 0.79801720f,
         0.67369562f, 0.52643216f, 0.36124167f, 0.18374951f],
        [0f, -0.43388373f, 0.64278758f, 0.34202015f],
        [0f, -0.20791170f, -0.40673664f, -0.58778524f,
         0.67369562f, 0.52643216f, 0.36124167f, 0.18374951f]
    ];

    public float[] Decode(AacTnsWindow window, AacTnsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Order < 0 || filter.Order > 20)
            throw new InvalidDataException("AAC TNS filter order is outside the supported range.");
        if (filter.Coefficients.Count != filter.Order)
            throw new InvalidDataException(
                "AAC TNS coefficient count does not match the filter order.");

        if (filter.Order == 0)
            return Array.Empty<float>();

        int tableIndex =
            (filter.CoefficientsAreCompressed ? 2 : 0) +
            (window.CoefficientResolution ? 1 : 0);
        float[] table = ReflectionCoefficientTables[tableIndex];
        int coefficientBits = (window.CoefficientResolution ? 4 : 3) -
                              (filter.CoefficientsAreCompressed ? 1 : 0);
        int tableSize = 1 << coefficientBits;

        float[] lpc = new float[filter.Order];
        for (int order = 1; order <= filter.Order; order++)
        {
            int index = filter.Coefficients[order - 1];
            if ((uint)index >= (uint)tableSize || index >= table.Length)
                throw new InvalidDataException(
                    "AAC TNS coefficient index exceeds its quantiser table.");

            float reflection = table[index];
            float[] previous = (float[])lpc.Clone();

            for (int i = 0; i < order - 1; i++)
                lpc[i] = previous[i] + reflection * previous[order - 2 - i];

            lpc[order - 1] = reflection;
        }

        return lpc;
    }
}
