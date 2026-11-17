using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads section_data() for one AAC individual channel stream.
/// </summary>
public sealed class AacSectionDataReader
{
    public IReadOnlyList<AacSectionInfo> Read(
        AacBitReader reader,
        AacIndividualChannelStreamInfo streamInfo)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(streamInfo);

        int maxScaleFactorBands = streamInfo.MaxScaleFactorBands;
        int maximumAllowedBands = streamInfo.UsesShortWindows ? 15 : 63;

        if (maxScaleFactorBands > maximumAllowedBands)
        {
            throw new InvalidDataException(
                "The AAC max_sfb value is outside the supported ICS range.");
        }

        int groupCount = streamInfo.WindowGroupCount;
        int sectionLengthBits = streamInfo.UsesShortWindows ? 3 : 5;
        int escapedSectionLength = (1 << sectionLengthBits) - 1;

        List<AacSectionInfo> sections = [];

        for (int group = 0; group < groupCount; group++)
        {
            int currentBand = 0;

            while (currentBand < maxScaleFactorBands)
            {
                int codebook = (int)reader.ReadBits(4);
                int sectionLength = 0;
                int increment;

                do
                {
                    increment = (int)reader.ReadBits(sectionLengthBits);
                    sectionLength = checked(sectionLength + increment);
                }
                while (increment == escapedSectionLength);

                if (sectionLength <= 0 ||
                    sectionLength > maxScaleFactorBands - currentBand)
                {
                    throw new InvalidDataException(
                        "The AAC section length is zero or exceeds max_sfb.");
                }

                sections.Add(new AacSectionInfo(
                    group,
                    currentBand,
                    sectionLength,
                    codebook));

                currentBand += sectionLength;
            }
        }

        return sections.AsReadOnly();
    }
}
