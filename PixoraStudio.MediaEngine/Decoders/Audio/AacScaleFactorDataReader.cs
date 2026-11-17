using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads scale_factor_data() for an AAC individual channel stream.
/// </summary>
public sealed class AacScaleFactorDataReader
{
    private const int NoiseEnergyOffset = 90;

    private readonly AacScaleFactorDecoder _decoder = new();

    public AacScaleFactorData Read(
        AacBitReader reader,
        IReadOnlyList<AacSectionInfo> sections,
        int globalGain)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sections);

        if (globalGain is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(globalGain));

        int spectralScaleFactor = globalGain;
        int noiseEnergy = globalGain - NoiseEnergyOffset;
        int intensityPosition = 0;
        bool firstNoiseBand = true;
        List<AacScaleFactorBandValue> values = [];

        foreach (AacSectionInfo section in sections)
        {
            ArgumentNullException.ThrowIfNull(section);

            if (section.WindowGroup < 0 ||
                section.StartScaleFactorBand < 0 ||
                section.ScaleFactorBandCount <= 0)
            {
                throw new InvalidDataException("AAC section metadata is invalid.");
            }

            int codebook = section.CodebookIndex;
            if (codebook is 12 or < 0)
                throw new InvalidDataException("AAC section uses a reserved codebook.");

            if (codebook is >= 1 and <= 11)
            {
                for (int band = section.StartScaleFactorBand;
                     band < section.EndScaleFactorBandExclusive;
                     band++)
                {
                    spectralScaleFactor += _decoder.DecodeDelta(reader);
                    if (spectralScaleFactor is < 0 or > 255)
                        throw new InvalidDataException(
                            $"AAC scalefactor is outside 0..255 at band {band}.");

                    values.Add(new AacScaleFactorBandValue(
                        section.WindowGroup, band, codebook, spectralScaleFactor));
                }
            }
            else if (codebook == 13)
            {
                for (int band = section.StartScaleFactorBand;
                     band < section.EndScaleFactorBandExclusive;
                     band++)
                {
                    if (firstNoiseBand)
                    {
                        noiseEnergy += (int)reader.ReadBits(9) - 256;
                        firstNoiseBand = false;
                    }
                    else
                    {
                        noiseEnergy += _decoder.DecodeDelta(reader);
                    }

                    values.Add(new AacScaleFactorBandValue(
                        section.WindowGroup, band, codebook,
                        Math.Clamp(noiseEnergy, -100, 155)));
                }
            }
            else if (codebook is 14 or 15)
            {
                for (int band = section.StartScaleFactorBand;
                     band < section.EndScaleFactorBandExclusive;
                     band++)
                {
                    intensityPosition += _decoder.DecodeDelta(reader);
                    values.Add(new AacScaleFactorBandValue(
                        section.WindowGroup, band, codebook,
                        Math.Clamp(intensityPosition, -155, 100)));
                }
            }
            // Codebook zero is the zero/noise-free band and carries no data.
        }

        return new AacScaleFactorData(values);
    }
}
