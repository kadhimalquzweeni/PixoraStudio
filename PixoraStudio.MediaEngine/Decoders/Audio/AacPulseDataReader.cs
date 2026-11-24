using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>Reads pulse_data() from an AAC individual channel stream.</summary>
public sealed class AacPulseDataReader
{
    public AacPulseData Read(AacBitReader reader, AacIndividualChannelStreamInfo streamInfo)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (streamInfo.UsesShortWindows)
            throw new InvalidDataException("AAC pulse data is not permitted for eight-short-window sequences.");

        int pulseCount = (int)reader.ReadBits(2) + 1;
        int startBand = (int)reader.ReadBits(6);
        List<AacPulse> pulses = new(pulseCount);
        int currentOffset = 0;

        for (int i = 0; i < pulseCount; i++)
        {
            currentOffset = checked(currentOffset + (int)reader.ReadBits(5));
            int amplitude = (int)reader.ReadBits(4);
            pulses.Add(new AacPulse(currentOffset, amplitude));
        }

        return new AacPulseData(startBand, pulses);
    }
}
