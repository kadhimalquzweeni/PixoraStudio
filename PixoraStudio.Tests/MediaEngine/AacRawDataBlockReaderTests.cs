using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
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
    public void ReadSingleChannelElement_ShouldSkipFillElementBeforeAudioElement()
    {
        byte[] accessUnit = BuildBits(
            (6, 3), (1, 4), (0xA5, 8),
            (0, 3), (5, 4), (0, 8),
            (0, 1), (0, 2), (0, 1), (0, 6), (0, 1),
            (0, 1), (0, 1), (0, 1),
            (7, 3));

        AacSingleChannelElement result = new AacRawDataBlockReader()
            .ReadSingleChannelElement(accessUnit, samplingFrequencyIndex: 4);

        Assert.Equal(5, result.ElementInstanceTag);
        Assert.Equal(AacWindowSequence.OnlyLongSequence,
            result.ChannelStream.StreamInfo.WindowSequence);
    }

    [Fact]
    public void ReadSingleChannelElement_ShouldSkipEscapedFillElementAndTrailingFillElement()
    {
        var fields = new List<(int Value, int Count)>
        {
            (6, 3), (15, 4), (2, 8)
        };

        for (int i = 0; i < 16; i++)
            fields.Add((i, 8));

        fields.AddRange(
        [
            (0, 3), (3, 4), (0, 8),
            (0, 1), (0, 2), (0, 1), (0, 6), (0, 1),
            (0, 1), (0, 1), (0, 1),
            (6, 3), (1, 4), (0x5A, 8),
            (7, 3)
        ]);

        AacSingleChannelElement result = new AacRawDataBlockReader()
            .ReadSingleChannelElement(BuildBits(fields.ToArray()), samplingFrequencyIndex: 4);

        Assert.Equal(3, result.ElementInstanceTag);
    }

    [Fact]
    public void ReadSingleChannelElement_ShouldSkipDataStreamElement()
    {
        byte[] accessUnit = BuildMonoWithDataStreamElement(
            byteAlign: false,
            count: 3,
            escapedCount: null);

        AacSingleChannelElement result = new AacRawDataBlockReader()
            .ReadSingleChannelElement(accessUnit, samplingFrequencyIndex: 4);

        Assert.Equal(2, result.ElementInstanceTag);
    }

    [Fact]
    public void ReadSingleChannelElement_ShouldSkipEscapedDataStreamElement()
    {
        byte[] accessUnit = BuildMonoWithDataStreamElement(
            byteAlign: false,
            count: 255,
            escapedCount: 2);

        AacSingleChannelElement result = new AacRawDataBlockReader()
            .ReadSingleChannelElement(accessUnit, samplingFrequencyIndex: 4);

        Assert.Equal(2, result.ElementInstanceTag);
    }

    [Fact]
    public void ReadSingleChannelElement_ShouldHonorDataStreamElementByteAlignment()
    {
        byte[] accessUnit = BuildMonoWithDataStreamElement(
            byteAlign: true,
            count: 2,
            escapedCount: null);

        AacSingleChannelElement result = new AacRawDataBlockReader()
            .ReadSingleChannelElement(accessUnit, samplingFrequencyIndex: 4);

        Assert.Equal(2, result.ElementInstanceTag);
    }

    [Fact]
    public void ReadSingleChannelElement_ShouldSkipProgramConfigElementBeforeAudioElement()
    {
        byte[] accessUnit = BuildMonoWithProgramConfigElement(
            programConfigBeforeAudio: true,
            includeOptionalFields: false);

        AacSingleChannelElement result = new AacRawDataBlockReader()
            .ReadSingleChannelElement(accessUnit, samplingFrequencyIndex: 4);

        Assert.Equal(2, result.ElementInstanceTag);
    }

    [Fact]
    public void ReadSingleChannelElement_ShouldSkipProgramConfigElementAfterAudioElement()
    {
        byte[] accessUnit = BuildMonoWithProgramConfigElement(
            programConfigBeforeAudio: false,
            includeOptionalFields: true);

        AacSingleChannelElement result = new AacRawDataBlockReader()
            .ReadSingleChannelElement(accessUnit, samplingFrequencyIndex: 4);

        Assert.Equal(2, result.ElementInstanceTag);
    }

    [Fact]
    public void ReadSingleChannelElement_ShouldRejectProgramConfigElementWithTruncatedComment()
    {
        byte[] accessUnit = BuildMonoWithProgramConfigElement(
            programConfigBeforeAudio: false,
            includeOptionalFields: true,
            commentFieldBytes: 255,
            commentBytesToWrite: 0);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new AacRawDataBlockReader()
                .ReadSingleChannelElement(accessUnit, samplingFrequencyIndex: 4));

        Assert.Contains("PCE comment declares 255 bytes", exception.Message);
        Assert.Contains("only", exception.Message);
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

    private static byte[] BuildMonoWithProgramConfigElement(
        bool programConfigBeforeAudio,
        bool includeOptionalFields,
        int commentFieldBytes = 2,
        int commentBytesToWrite = 2)
    {
        List<(int Value, int Count)> fields = [];

        if (programConfigBeforeAudio)
            AddProgramConfigElement(fields, includeOptionalFields, commentFieldBytes, commentBytesToWrite);

        fields.AddRange(
        [
            (0, 3), (2, 4), (0, 8),
            (0, 1), (0, 2), (0, 1), (0, 6), (0, 1),
            (0, 1), (0, 1), (0, 1)
        ]);

        if (!programConfigBeforeAudio)
            AddProgramConfigElement(fields, includeOptionalFields, commentFieldBytes, commentBytesToWrite);

        fields.Add((7, 3));
        return BuildBits(fields.ToArray());
    }

    private static void AddProgramConfigElement(
        List<(int Value, int Count)> fields,
        bool includeOptionalFields,
        int commentFieldBytes,
        int commentBytesToWrite)
    {
        fields.AddRange(
        [
            (5, 3), // ID_PCE
            (1, 4), // element_instance_tag
            (1, 2), // object_type
            (4, 4), // sampling_frequency_index
            (includeOptionalFields ? 1 : 0, 4), // front elements
            (includeOptionalFields ? 1 : 0, 4), // side elements
            (includeOptionalFields ? 1 : 0, 4), // back elements
            (includeOptionalFields ? 1 : 0, 2), // LFE elements
            (includeOptionalFields ? 1 : 0, 3), // associated data elements
            (includeOptionalFields ? 1 : 0, 4), // valid CC elements
            (includeOptionalFields ? 1 : 0, 1), // mono mixdown present
        ]);

        if (includeOptionalFields)
            fields.Add((2, 4));

        fields.Add((includeOptionalFields ? 1 : 0, 1)); // stereo mixdown present
        if (includeOptionalFields)
            fields.Add((3, 4));

        fields.Add((includeOptionalFields ? 1 : 0, 1)); // matrix mixdown present
        if (includeOptionalFields)
            fields.Add((5, 3));

        if (includeOptionalFields)
        {
            fields.AddRange(
            [
                (1, 1), (4, 4), // front: CPE, tag
                (0, 1), (5, 4), // side: SCE, tag
                (1, 1), (6, 4), // back: CPE, tag
                (7, 4), // LFE tag
                (8, 4), // associated-data tag
                (1, 1), (9, 4) // valid-CC: independent switch, tag
            ]);
        }

        while ((fields.Sum(field => field.Count) & 7) != 0)
            fields.Add((1, 1)); // Alignment bits are skipped without constraining their values.

        fields.Add((commentFieldBytes, 8)); // comment_field_bytes

        for (int i = 0; i < commentBytesToWrite; i++)
            fields.Add((i == 0 ? 0xA5 : 0x5A, 8));
    }

    private static byte[] BuildMonoWithDataStreamElement(
        bool byteAlign,
        int count,
        int? escapedCount)
    {
        List<(int Value, int Count)> fields =
        [
            (0, 3), (2, 4), (0, 8),
            (0, 1), (0, 2), (0, 1), (0, 6), (0, 1),
            (0, 1), (0, 1), (0, 1),
            (4, 3), (7, 4), (byteAlign ? 1 : 0, 1),
            (count, 8)
        ];

        if (escapedCount.HasValue)
            fields.Add((escapedCount.Value, 8));

        if (byteAlign)
        {
            int bitLength = fields.Sum(field => field.Count);
            int padding = (8 - (bitLength & 7)) & 7;
            for (int i = 0; i < padding; i++)
                fields.Add((1, 1));
        }

        int payloadLength = escapedCount.HasValue
            ? count + escapedCount.Value
            : count;

        for (int i = 0; i < payloadLength; i++)
            fields.Add((i & 0xFF, 8));

        fields.Add((7, 3));
        return BuildBits(fields.ToArray());
    }

    private static byte[] BuildBits(params (int Value, int Count)[] fields)
    {
        List<int> bits = [];
        foreach ((int value, int count) in fields)
            for (int bit = count - 1; bit >= 0; bit--)
                bits.Add((value >> bit) & 1);

        while ((bits.Count & 7) != 0)
            bits.Add(1);

        byte[] result = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++)
            result[i / 8] |= (byte)(bits[i] << (7 - (i & 7)));

        return result;
    }
}
