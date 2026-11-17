
using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads AAC individual_channel_stream information.
/// </summary>
public sealed class AacIndividualChannelStreamInfoReader
{
    public AacIndividualChannelStreamInfo Read(AacBitReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // The reserved bit must be zero.
        uint reservedBit = reader.ReadBits(1);

        if (reservedBit != 0)
        {
            throw new InvalidDataException(
                "The AAC ICS reserved bit must be zero.");
        }

        AacWindowSequence windowSequence =
            (AacWindowSequence)reader.ReadBits(2);

        AacWindowShape windowShape =
            (AacWindowShape)reader.ReadBits(1);

        byte maxScaleFactorBands;
        byte scaleFactorGrouping = 0;
        bool predictorDataPresent = false;

        if (windowSequence == AacWindowSequence.EightShortSequence)
        {
            maxScaleFactorBands = (byte)reader.ReadBits(4);
            scaleFactorGrouping = (byte)reader.ReadBits(7);
        }
        else
        {
            maxScaleFactorBands = (byte)reader.ReadBits(6);
            predictorDataPresent = reader.ReadBits(1) != 0;

            if (predictorDataPresent)
            {
                throw new NotSupportedException(
                    "AAC prediction data is not supported by the AAC-LC decoder.");
            }
        }

        return new AacIndividualChannelStreamInfo(
            windowSequence,
            windowShape,
            maxScaleFactorBands,
            scaleFactorGrouping,
            predictorDataPresent);
    }
}
