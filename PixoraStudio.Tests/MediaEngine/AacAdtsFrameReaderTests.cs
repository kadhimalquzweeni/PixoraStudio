
using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacAdtsFrameReaderTests
{
    private readonly AacAdtsFrameReader _reader = new();

    [Fact]
    public void Read_ShouldParseAacLcStereoFrame()
    {
        byte[] data = CreateAdtsFrame(
            payload: [0x11, 0x22, 0x33],
            samplingFrequencyIndex: 4,
            channelConfiguration: 2);

        AacAdtsFrame result = _reader.Read(data);

        Assert.Equal((byte)0, result.MpegId);
        Assert.Equal((byte)1, result.Profile);
        Assert.Equal((byte)4, result.SamplingFrequencyIndex);
        Assert.Equal(44100u, result.SamplingFrequency);
        Assert.Equal((byte)2, result.ChannelConfiguration);
        Assert.Equal(2, result.ChannelCount);
        Assert.Equal(7, result.HeaderLength);
        Assert.Equal(data.Length, result.FrameLength);
        Assert.Equal(1, result.RawDataBlockCount);
        Assert.False(result.HasCrc);
        Assert.Equal(
            new byte[] { 0x11, 0x22, 0x33 },
            result.Payload.ToArray());
    }

    [Fact]
    public void Read_ShouldParseCrcProtectedFrame()
    {
        byte[] data = CreateAdtsFrame(
            payload: [0xAA, 0xBB],
            protectionAbsent: false,
            crc: [0x12, 0x34]);

        AacAdtsFrame result = _reader.Read(data);

        Assert.True(result.HasCrc);
        Assert.Equal(9, result.HeaderLength);
        Assert.Equal(
            new byte[] { 0xAA, 0xBB },
            result.Payload.ToArray());
    }

    [Theory]
    [InlineData(0, 96000)]
    [InlineData(1, 88200)]
    [InlineData(2, 64000)]
    [InlineData(3, 48000)]
    [InlineData(4, 44100)]
    [InlineData(5, 32000)]
    [InlineData(6, 24000)]
    [InlineData(7, 22050)]
    [InlineData(8, 16000)]
    [InlineData(9, 12000)]
    [InlineData(10, 11025)]
    [InlineData(11, 8000)]
    [InlineData(12, 7350)]
    public void Read_ShouldResolveSamplingFrequency(
        int index,
        uint expectedFrequency)
    {
        byte[] data = CreateAdtsFrame(
            payload: [0x01],
            samplingFrequencyIndex: (byte)index);

        AacAdtsFrame result = _reader.Read(data);

        Assert.Equal(expectedFrequency, result.SamplingFrequency);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 8)]
    public void Read_ShouldResolveChannelConfiguration(
        int configuration,
        int expectedChannels)
    {
        byte[] data = CreateAdtsFrame(
            payload: [0x01],
            channelConfiguration: (byte)configuration);

        AacAdtsFrame result = _reader.Read(data);

        Assert.Equal(expectedChannels, result.ChannelCount);
    }

    [Fact]
    public void Read_ShouldIgnoreTrailingDataAfterFrame()
    {
        byte[] frame = CreateAdtsFrame([0x11, 0x22]);

        byte[] data =
        [
            .. frame,
            0xFF,
            0xFF
        ];

        AacAdtsFrame result = _reader.Read(data);

        Assert.Equal(frame.Length, result.FrameLength);
        Assert.Equal(new byte[] { 0x11, 0x22 }, result.Payload.ToArray());
    }

    [Fact]
    public void Read_ShouldRejectIncompleteHeader()
    {
        Assert.Throws<EndOfStreamException>(
            () => _reader.Read(new byte[6]));
    }

    [Fact]
    public void Read_ShouldRejectInvalidSyncWord()
    {
        byte[] data = CreateAdtsFrame([0x01]);
        data[0] = 0x00;

        Assert.Throws<InvalidDataException>(
            () => _reader.Read(data));
    }

    [Fact]
    public void Read_ShouldRejectNonZeroLayer()
    {
        byte[] data = CreateAdtsFrame([0x01]);

        // Layer occupies bits 2 and 3 of byte 1.
        data[1] |= 0x04;

        Assert.Throws<InvalidDataException>(
            () => _reader.Read(data));
    }

    [Fact]
    public void Read_ShouldRejectMpegTwo()
    {
        byte[] data = CreateAdtsFrame([0x01]);

        // MPEG ID is bit 3 of byte 1.
        data[1] |= 0x08;

        Assert.Throws<NotSupportedException>(
            () => _reader.Read(data));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void Read_ShouldRejectUnsupportedProfile(byte profile)
    {
        byte[] data = CreateAdtsFrame(
            [0x01],
            profile: profile);

        Assert.Throws<NotSupportedException>(
            () => _reader.Read(data));
    }

    [Fact]
    public void Read_ShouldRejectReservedSamplingFrequency()
    {
        byte[] data = CreateAdtsFrame(
            [0x01],
            samplingFrequencyIndex: 13);

        Assert.Throws<InvalidDataException>(
            () => _reader.Read(data));
    }

    [Fact]
    public void Read_ShouldRejectChannelConfigurationZero()
    {
        byte[] data = CreateAdtsFrame(
            [0x01],
            channelConfiguration: 0);

        Assert.Throws<NotSupportedException>(
            () => _reader.Read(data));
    }

    [Fact]
    public void Read_ShouldRejectReservedChannelConfiguration()
    {
        byte[] data = CreateAdtsFrame([0x01]);

        // Channel configuration 8 cannot be represented in the
        // three-bit ADTS field. Test the reserved value by setting
        // the field to its maximum representable value only if the
        // specification defines it as reserved.
        // ADTS configurations 1–7 are valid; configuration 0
        // requires PCE.
    }

    [Fact]
    public void Read_ShouldRejectMultipleRawDataBlocks()
    {
        byte[] data = CreateAdtsFrame(
            [0x01],
            rawDataBlockCount: 2);

        Assert.Throws<NotSupportedException>(
            () => _reader.Read(data));
    }

    [Fact]
    public void Read_ShouldRejectFrameShorterThanHeader()
    {
        byte[] data = CreateAdtsFrame(
            [0x01],
            declaredFrameLength: 6);

        Assert.Throws<InvalidDataException>(
            () => _reader.Read(data));
    }

    [Fact]
    public void Read_ShouldRejectIncompleteFrame()
    {
        byte[] data = CreateAdtsFrame([0x11, 0x22, 0x33]);

        byte[] truncated = data[..^1];

        Assert.Throws<EndOfStreamException>(
            () => _reader.Read(truncated));
    }

    private static byte[] CreateAdtsFrame(
        byte[] payload,
        byte samplingFrequencyIndex = 4,
        byte channelConfiguration = 2,
        byte profile = 1,
        bool protectionAbsent = true,
        byte[]? crc = null,
        int rawDataBlockCount = 1, int? declaredFrameLength = null)
    {
        int headerLength = protectionAbsent ? 7 : 9;

        if (!protectionAbsent && (crc is null || crc.Length != 2))
        {
            throw new ArgumentException(
                "A two-byte CRC is required when protection is enabled.",
                nameof(crc));
        }

        int frameLength = headerLength + payload.Length;

        if (frameLength > 0x1FFF)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        if (rawDataBlockCount is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(rawDataBlockCount));
        }

        if (channelConfiguration > 7)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelConfiguration));
        }
        List<int> bits = [];

        void WriteBits(uint value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                bits.Add((int)((value >> i) & 1));
            }
        }

        WriteBits(0xFFF, 12);
        WriteBits(0, 1); // MPEG-4.
        WriteBits(0, 2); // Layer.
        WriteBits(protectionAbsent ? 1u : 0u, 1);
        WriteBits(profile, 2);
        WriteBits(samplingFrequencyIndex, 4);
        WriteBits(0, 1); // Private bit.
        WriteBits(channelConfiguration, 3);
        WriteBits(0, 1); // Originality.
        WriteBits(0, 1); // Home.
        WriteBits(0, 1); // Copyright identification bit.
        WriteBits(0, 1); // Copyright identification start.
        WriteBits((uint)(declaredFrameLength ?? frameLength), 13);
        WriteBits(0x7FF, 11); // Variable bitrate.
        WriteBits((uint)(rawDataBlockCount - 1), 2);

        byte[] result = new byte[frameLength];

        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i] != 0)
            {
                result[i / 8] |= (byte)(1 << (7 - (i % 8)));
            }
        }

        int payloadOffset = 7;

        if (!protectionAbsent)
        {
            crc!.CopyTo(result, 7);
            payloadOffset = 9;
        }

        payload.CopyTo(result, payloadOffset);

        return result;
    }
}
