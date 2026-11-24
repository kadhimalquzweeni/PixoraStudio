using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads an AAC channel_pair_element (CPE), including common ICS information
/// and expansion of ms_used flags to spectral-line masks.
/// </summary>
public sealed class AacChannelPairElementReader
{
    private const int ChannelPairElementId = 1;

    private readonly AacIndividualChannelStreamInfoReader _infoReader = new();
    private readonly AacIndividualChannelStreamDataReader _channelReader = new();

    public AacChannelPairElement Read(
        AacBitReader reader,
        int samplingFrequencyIndex)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if ((int)reader.ReadBits(3) != ChannelPairElementId)
            throw new InvalidDataException("The AAC element is not a channel_pair_element.");

        return ReadAfterElementId(reader, samplingFrequencyIndex);
    }

    /// <summary>
    /// Reads a CPE payload when its three-bit element identifier has already
    /// been consumed by an enclosing raw_data_block reader.
    /// </summary>
    public AacChannelPairElement ReadAfterElementId(
        AacBitReader reader,
        int samplingFrequencyIndex)
    {
        ArgumentNullException.ThrowIfNull(reader);

        byte tag = (byte)reader.ReadBits(4);
        bool commonWindow = reader.ReadBits(1) != 0;

        AacIndividualChannelStreamInfo? sharedInfo = null;
        bool[] msMask = new bool[1024];

        if (commonWindow)
        {
            sharedInfo = _infoReader.Read(reader);
            IReadOnlyList<int> offsets = sharedInfo.UsesShortWindows
                ? AacScaleFactorBandTable.GetShortWindowOffsets(samplingFrequencyIndex)
                : AacScaleFactorBandTable.GetLongWindowOffsets(samplingFrequencyIndex);

            if (sharedInfo.MaxScaleFactorBands > offsets.Count - 1)
                throw new InvalidDataException(
                    "AAC common-window max_sfb exceeds the scalefactor-band table.");

            int maskPresent = (int)reader.ReadBits(2);
            if (maskPresent == 3)
                throw new InvalidDataException("AAC ms_mask_present value 3 is reserved.");

            for (int group = 0; group < sharedInfo.WindowGroupCount; group++)
            {
                for (int band = 0; band < sharedInfo.MaxScaleFactorBands; band++)
                {
                    bool used = maskPresent switch
                    {
                        0 => false,
                        1 => reader.ReadBits(1) != 0,
                        2 => true,
                        _ => false
                    };

                    if (!used)
                        continue;

                    int groupFirstWindow = 0;
                    for (int previous = 0; previous < group; previous++)
                        groupFirstWindow += sharedInfo.WindowGroupLengths[previous];

                    int bandStart = offsets[band];
                    int bandWidth = offsets[band + 1] - bandStart;
                    int windowLength = sharedInfo.UsesShortWindows ? 128 : 1024;

                    for (int window = 0; window < sharedInfo.WindowGroupLengths[group]; window++)
                    {
                        int destination = ((groupFirstWindow + window) * windowLength) + bandStart;
                        Array.Fill(msMask, true, destination, bandWidth);
                    }
                }
            }
        }

        AacIndividualChannelStreamData left =
            _channelReader.Read(reader, samplingFrequencyIndex, commonWindow, sharedInfo);
        AacIndividualChannelStreamData right =
            _channelReader.Read(reader, samplingFrequencyIndex, commonWindow, sharedInfo);

        if (left.ScaleFactors.Bands.Any(
                value => value.CodebookIndex is 14 or 15))
        {
            throw new InvalidDataException(
                "AAC intensity stereo codebooks are only valid in the right channel.");
        }

        if (!commonWindow && right.ScaleFactors.Bands.Any(
                value => value.CodebookIndex is 14 or 15))
        {
            throw new InvalidDataException(
                "AAC intensity stereo requires common_window to be set.");
        }

        return new AacChannelPairElement(tag, commonWindow, left, right, msMask);
    }
}
