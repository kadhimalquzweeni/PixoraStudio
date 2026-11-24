using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Creates the symmetric AAC sine and Kaiser-Bessel-derived analysis/synthesis
/// windows for long (2048) and short (256) IMDCT blocks.
/// </summary>
public static class AacWindowFunction
{
    public const int LongWindowLength = 2048;
    public const int ShortWindowLength = 256;

    private static readonly Lazy<float[]> LongSine =
        new(() => CreateSine(LongWindowLength));
    private static readonly Lazy<float[]> ShortSine =
        new(() => CreateSine(ShortWindowLength));
    private static readonly Lazy<float[]> LongKbd =
        new(() => CreateKaiserBesselDerived(LongWindowLength, 4.0));
    private static readonly Lazy<float[]> ShortKbd =
        new(() => CreateKaiserBesselDerived(ShortWindowLength, 6.0));

    public static float[] Create(
        AacWindowShape shape,
        bool shortWindow = false)
    {
        float[] cached = (shape, shortWindow) switch
        {
            (AacWindowShape.Sine, false) => LongSine.Value,
            (AacWindowShape.Sine, true) => ShortSine.Value,
            (AacWindowShape.KaiserBesselDerived, false) => LongKbd.Value,
            (AacWindowShape.KaiserBesselDerived, true) => ShortKbd.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        return cached.ToArray();
    }

    private static float[] CreateSine(int length)
    {
        float[] window = new float[length];

        for (int i = 0; i < length; i++)
            window[i] = (float)Math.Sin(Math.PI * (i + 0.5) / length);

        return window;
    }

    private static float[] CreateKaiserBesselDerived(
        int length,
        double alpha)
    {
        int half = length / 2;
        double[] kaiser = new double[half + 1];
        double total = 0;

        for (int i = 0; i <= half; i++)
        {
            double position = (2.0 * i / half) - 1.0;
            double argument = Math.PI * alpha *
                Math.Sqrt(Math.Max(0, 1.0 - (position * position)));

            kaiser[i] = BesselI0(argument);
            total += kaiser[i];
        }

        float[] window = new float[length];
        double cumulative = 0;

        for (int i = 0; i < half; i++)
        {
            cumulative += kaiser[i];
            float value = (float)Math.Sqrt(cumulative / total);
            window[i] = value;
            window[length - 1 - i] = value;
        }

        float centre = (float)Math.Sqrt((total - kaiser[half]) / total);
        window[half - 1] = centre;
        window[half] = centre;

        return window;
    }

    private static double BesselI0(double value)
    {
        double sum = 1;
        double term = 1;
        double quarterSquare = value * value / 4.0;

        for (int k = 1; k <= 40; k++)
        {
            term *= quarterSquare / (k * (double)k);
            sum += term;

            if (term <= sum * 1e-16)
                break;
        }

        return sum;
    }
}
