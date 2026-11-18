using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Decodes a complete CAVLC residual block from an H.264 RBSP bitstream.</summary>
public sealed class H264CavlcResidualBlockReader
{
    private readonly H264CavlcVlcDecoder _vlcDecoder;

    public H264CavlcResidualBlockReader()
        : this(new H264CavlcVlcDecoder())
    {
    }

    public H264CavlcResidualBlockReader(H264CavlcVlcDecoder vlcDecoder)
    {
        ArgumentNullException.ThrowIfNull(vlcDecoder);
        _vlcDecoder = vlcDecoder;
    }

    /// <summary>
    /// Reads coeff_token, trailing-one signs, levels, total_zeros and run_before.
    /// The returned coefficient array is in forward scan order.
    /// </summary>
    public H264CavlcResidualBlock ReadBlock(
        H264BitReader reader,
        int predictedNonZeroCount,
        int maximumCoefficients = 16)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (maximumCoefficients is not (4 or 8 or 15 or 16))
            throw new ArgumentOutOfRangeException(nameof(maximumCoefficients));

        H264CoeffToken token = _vlcDecoder.DecodeCoeffToken(
            reader, predictedNonZeroCount, maximumCoefficients);

        int[] coefficients = new int[maximumCoefficients];
        if (token.TotalCoefficients == 0)
            return new H264CavlcResidualBlock(maximumCoefficients, 0, 0, 0, coefficients);

        int[] levels = DecodeLevels(reader, token.TotalCoefficients, token.TrailingOnes);
        int totalZeros = token.TotalCoefficients == maximumCoefficients
            ? 0
            : _vlcDecoder.DecodeTotalZeros(reader, token.TotalCoefficients, maximumCoefficients);

        int[] runs = new int[token.TotalCoefficients];
        int zerosLeft = totalZeros;
        for (int i = 0; i < token.TotalCoefficients - 1; i++)
        {
            int run = zerosLeft > 0 ? _vlcDecoder.DecodeRunBefore(reader, zerosLeft) : 0;
            runs[i] = run;
            zerosLeft -= run;
        }
        runs[^1] = zerosLeft;

        int coefficientNumber = -1;
        for (int i = token.TotalCoefficients - 1; i >= 0; i--)
        {
            coefficientNumber = checked(coefficientNumber + runs[i] + 1);
            if ((uint)coefficientNumber >= (uint)maximumCoefficients)
                throw new FormatException("CAVLC run lengths place a coefficient outside the residual block.");
            coefficients[coefficientNumber] = levels[i];
        }

        return new H264CavlcResidualBlock(
            maximumCoefficients, token.TotalCoefficients, token.TrailingOnes,
            totalZeros, coefficients);
    }

    private static int[] DecodeLevels(H264BitReader reader, int totalCoefficients, int trailingOnes)
    {
        int[] levels = new int[totalCoefficients];

        for (int i = 0; i < trailingOnes; i++)
            levels[i] = reader.ReadFlag() ? -1 : 1;

        int suffixLength = totalCoefficients > 10 && trailingOnes < 3 ? 1 : 0;
        for (int i = trailingOnes; i < totalCoefficients; i++)
        {
            int prefix = 0;
            while (!reader.ReadFlag())
            {
                prefix++;
                if (prefix > 28)
                    throw new FormatException("CAVLC level_prefix exceeds the supported 32-bit range.");
            }

            int suffixSize = prefix == 14 && suffixLength == 0
                ? 4
                : prefix >= 15 ? prefix - 3 : suffixLength;
            if (suffixSize is < 0 or > 32)
                throw new FormatException("CAVLC level_suffix has an invalid length.");

            uint suffix = suffixSize == 0 ? 0 : reader.ReadBits(suffixSize);
            long levelCode = ((long)Math.Min(prefix, 15) << suffixLength) + suffix;

            if (prefix >= 15 && suffixLength == 0)
                levelCode += 15;
            if (prefix >= 16)
                levelCode += (1L << (prefix - 3)) - 4096;

            if (i == trailingOnes && trailingOnes < 3)
                levelCode += 2;

            if (levelCode > int.MaxValue)
                throw new FormatException("Decoded CAVLC coefficient level exceeds Int32.");

            int level = (levelCode & 1) == 0
                ? checked((int)((levelCode + 2) >> 1))
                : checked(-(int)((levelCode + 1) >> 1));
            if (level == 0)
                throw new FormatException("CAVLC decoded a zero coefficient level.");

            levels[i] = level;
            if (suffixLength == 0)
                suffixLength = 1;
            if (suffixLength < 6 && Math.Abs((long)level) > (3L << (suffixLength - 1)))
                suffixLength++;
        }

        return levels;
    }
}
