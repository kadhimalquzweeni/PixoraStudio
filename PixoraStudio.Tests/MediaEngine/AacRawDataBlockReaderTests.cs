using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacRawDataBlockReaderTests
{
    [Fact]
    public void ReadSingleChannelElement_ShouldReadEmptyLongWindowIcsAndEndMarker()
    {
        byte[] accessUnit = BuildEmptyMonoAccessUnit(tag: 5);

        var result = new AacRawDataBlockReader()
            .ReadSingleChannelElement(accessUnit, samplingFrequencyIndex: 4);

        Assert.Equal(5, result.ElementInstanceTag);
        Assert.Equal(AacWindowSequence.OnlyLongSequence,
            result.ChannelStream.StreamInfo.WindowSequence);
        Assert.Empty(result.ChannelStream.SpectralBands);
    }

    [Fact]
    public void ReadChannelPairElement_ShouldReadCommonWindowStereoIcs()
    {
        AacChannelPairElement pair = new AacRawDataBlockReader()
            .ReadChannelPairElement(
                BuildEmptyStereoAccessUnit(),
                samplingFrequencyIndex: 4);

        Assert.True(pair.CommonWindow);
        Assert.Equal(AacWindowSequence.OnlyLongSequence,
            pair.LeftChannel.StreamInfo.WindowSequence);
        Assert.Equal(pair.LeftChannel.StreamInfo, pair.RightChannel.StreamInfo);
        Assert.Equal(1024, pair.MidSideMask.Length);
        Assert.DoesNotContain(true, pair.MidSideMask);
    }

    [Fact]
    public void ReadSingleChannelElement_ShouldRejectNonSceElement()
    {
        byte[] data = BuildBits((1, 3), (0, 5));

        Assert.Throws<NotSupportedException>(() =>
            new AacRawDataBlockReader().ReadSingleChannelElement(data, 4));
    }

    [Fact]
    public void ReadSingleChannelElement_ShouldRejectUnexpectedFollowingElement()
    {
        byte[] data = BuildBits(
            (0, 3), (0, 4), (0, 8),
            (0, 1), (0, 2), (0, 1), (0, 6), (0, 1),
            (0, 1), (0, 1), (0, 1),
            (1, 3), (0, 5));

        Assert.Throws<NotSupportedException>(() =>
            new AacRawDataBlockReader().ReadSingleChannelElement(data, 4));
    }

    internal static byte[] BuildEmptyMonoAccessUnit(byte tag = 0) =>
        BuildBits(
            (0, 3), (tag, 4), (0, 8),
            (0, 1), (0, 2), (0, 1), (0, 6), (0, 1),
            (0, 1), (0, 1), (0, 1),
            (7, 3));

    internal static byte[] BuildEmptyStereoAccessUnit() =>
        BuildBits(
            (1, 3), (0, 4), (1, 1),
            (0, 1), (0, 2), (0, 1), (0, 6), (0, 1),
            (0, 2),
            (0, 8), (0, 1), (0, 1), (0, 1),
            (0, 8), (0, 1), (0, 1), (0, 1),
            (7, 3));

    private static byte[] BuildBits(params (int Value, int Count)[] fields)
    {
        List<int> bits = [];
        foreach ((int value, int count) in fields)
            for (int bit = count - 1; bit >= 0; bit--)
                bits.Add((value >> bit) & 1);

        while ((bits.Count & 7) != 0)
            bits.Add(0);

        byte[] result = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++)
            result[i / 8] |= (byte)(bits[i] << (7 - (i & 7)));

        return result;
    }
}
