using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>Reads tns_data() syntax for long- and short-window AAC ICS data.</summary>
public sealed class AacTnsDataReader
{
    public AacTnsData Read(AacBitReader reader, AacIndividualChannelStreamInfo streamInfo)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(streamInfo);

        bool shortWindows = streamInfo.UsesShortWindows;
        int windowCount = shortWindows ? 8 : 1;
        int filterCountBits = shortWindows ? 1 : 2;
        int lengthBits = shortWindows ? 4 : 6;
        int orderBits = shortWindows ? 3 : 5;
        List<AacTnsWindow> windows = new(windowCount);

        for (int window = 0; window < windowCount; window++)
        {
            int filterCount = (int)reader.ReadBits(filterCountBits);
            bool coefficientResolution = filterCount > 0 && reader.ReadBits(1) != 0;
            List<AacTnsFilter> filters = new(filterCount);

            for (int filterIndex = 0; filterIndex < filterCount; filterIndex++)
            {
                int length = (int)reader.ReadBits(lengthBits);
                int order = (int)reader.ReadBits(orderBits);

                if (order > (shortWindows ? 7 : 12))
                    throw new InvalidDataException("AAC TNS filter order exceeds the permitted limit.");

                if (order == 0)
                {
                    filters.Add(new AacTnsFilter(length, 0, false, false, Array.Empty<int>()));
                    continue;
                }

                bool directionReversed = reader.ReadBits(1) != 0;
                bool compressed = reader.ReadBits(1) != 0;
                int coefficientBits = (coefficientResolution ? 4 : 3) - (compressed ? 1 : 0);
                List<int> coefficients = new(order);

                for (int i = 0; i < order; i++)
                    coefficients.Add((int)reader.ReadBits(coefficientBits));

                filters.Add(new AacTnsFilter(
                    length, order, directionReversed, compressed,
                    Array.AsReadOnly(coefficients.ToArray())));
            }

            windows.Add(new AacTnsWindow(
                coefficientResolution,
                Array.AsReadOnly(filters.ToArray())));
        }

        return new AacTnsData(windows);
    }
}
