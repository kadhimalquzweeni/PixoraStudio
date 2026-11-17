using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Decodes all spectral sections in an AAC ICS using the active scalefactor-band
/// offsets and window-group lengths.
/// </summary>
public sealed class AacSpectralSectionDecoder
{
    private readonly AacSpectralDataDecoder _bandDecoder = new();

    public IReadOnlyList<AacSpectralBandData> Decode(
        AacBitReader reader,
        IReadOnlyList<AacSectionInfo> sections,
        IReadOnlyList<int> scaleFactorBandOffsets,
        IReadOnlyList<int> windowGroupLengths)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(scaleFactorBandOffsets);
        ArgumentNullException.ThrowIfNull(windowGroupLengths);

        if (scaleFactorBandOffsets.Count < 2)
            throw new ArgumentException("At least two scalefactor-band offsets are required.", nameof(scaleFactorBandOffsets));
        if (windowGroupLengths.Count == 0 || windowGroupLengths.Any(x => x <= 0))
            throw new ArgumentException("Window groups must contain positive window counts.", nameof(windowGroupLengths));

        for (int i = 1; i < scaleFactorBandOffsets.Count; i++)
        {
            if (scaleFactorBandOffsets[i] <= scaleFactorBandOffsets[i - 1])
                throw new ArgumentException("Scalefactor-band offsets must be strictly increasing.", nameof(scaleFactorBandOffsets));
        }

        List<AacSpectralBandData> result = [];

        foreach (AacSectionInfo section in sections)
        {
            ArgumentNullException.ThrowIfNull(section);

            int sectionStartPosition = reader.PositionBits;

            if ((uint)section.WindowGroup >= (uint)windowGroupLengths.Count)
                throw new InvalidDataException("AAC section references a missing window group.");
            if (section.StartScaleFactorBand < 0 ||
                section.ScaleFactorBandCount <= 0 ||
                section.EndScaleFactorBandExclusive >= scaleFactorBandOffsets.Count)
                throw new InvalidDataException("AAC section references invalid scalefactor bands.");
            if (section.CodebookIndex is < 0 or > 15 || section.CodebookIndex == 12)
                throw new InvalidDataException("AAC section uses a reserved codebook.");

            for (int band = section.StartScaleFactorBand;
                 band < section.EndScaleFactorBandExclusive;
                 band++)
            {
                int bandWidth = scaleFactorBandOffsets[band + 1] - scaleFactorBandOffsets[band];
                int coefficientCount = checked(bandWidth * windowGroupLengths[section.WindowGroup]);
                int[] coefficients;

                if (section.CodebookIndex is >= 1 and <= 11)
                {
                    coefficients = _bandDecoder.DecodeBand(
                        reader, section.CodebookIndex, coefficientCount);
                }
                else
                {
                    // Zero, PNS, and intensity sections carry no spectral Huffman data.
                    coefficients = new int[coefficientCount];
                }

                result.Add(new AacSpectralBandData(
                    section.WindowGroup, band, section.CodebookIndex, coefficients));
            }

            System.Diagnostics.Debug.WriteLine(
                $"AAC spectral section: group={section.WindowGroup}, " +
                $"bands={section.StartScaleFactorBand}-{section.EndScaleFactorBandExclusive - 1}, " +
                $"codebook={section.CodebookIndex}, " +
                $"coefficients={section.ScaleFactorBandCount}, " +
                $"start={sectionStartPosition}, end={reader.PositionBits}, " +
                $"bits={reader.PositionBits - sectionStartPosition}");
        }

        return result.AsReadOnly();
    }
}
