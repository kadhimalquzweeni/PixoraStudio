using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.Audio;
using PixoraStudio.MediaEngine.Readers.Mp4;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4AudioSampleEntryReaderTests
{
    private readonly Mp4AudioSampleEntryReader _reader = new();

    [Fact]
    public void Read_ShouldParseAacAudioSampleEntry()
    {
        using MemoryStream stream = CreateSampleEntry(
            out Mp4Box entry);

        var result = _reader.Read(stream, entry);

        Assert.Equal("mp4a", result.Format);
        Assert.Equal((ushort)1, result.DataReferenceIndex);
        Assert.Equal((ushort)0, result.Version);
        Assert.Equal((ushort)2, result.ChannelCount);
        Assert.Equal((ushort)16, result.SampleSize);
        Assert.Equal(44100, result.SampleRate);
        Assert.Equal(
            Mp4AudioCodec.Aac,
            result.Codec);

        Assert.NotNull(result.AudioSpecificConfig);
        Assert.Equal(
            (byte)2,
            result.AudioSpecificConfig.AudioObjectType);
        Assert.Equal(
            (uint)44100,
            result.AudioSpecificConfig.SamplingFrequency);
        Assert.Equal(
            (byte)2,
            result.AudioSpecificConfig.ChannelConfiguration);
        Assert.Equal(
            2,
            result.AudioSpecificConfig.ChannelCount);
    }

    [Fact]
    public void Read_ShouldRestoreStreamPosition()
    {
        using MemoryStream stream = CreateSampleEntry(
            out Mp4Box entry);

        stream.Position = 3;

        _reader.Read(stream, entry);

        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedSampleEntryType()
    {
        using MemoryStream stream = new([]);

        Mp4Box entry = new(
            8,
            "ac-3",
            0,
            8);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, entry));
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedSampleEntryVersion()
    {
        using MemoryStream stream = CreateSampleEntry(
            out Mp4Box entry);

        stream.Position = entry.DataOffset + 8;
        stream.WriteByte(0);
        stream.WriteByte(1);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, entry));
    }

    [Fact]
    public void Read_ShouldRejectMissingEsdsBox()
    {
        byte[] entryBytes = CreateBox(
            "mp4a",
            new byte[28]);

        using MemoryStream stream = new(entryBytes);

        Mp4Box entry = new(
            (ulong)entryBytes.Length,
            "mp4a",
            0,
            8);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, entry));
    }

    [Fact]
    public void Read_ShouldRejectNonAacDecoderConfiguration()
    {
        byte[] esds = CreateEsdsBox(
            objectTypeIndication: 0x66);

        using MemoryStream stream = CreateSampleEntry(
            esds,
            out Mp4Box entry);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, entry));
    }

    private static MemoryStream CreateSampleEntry(
        out Mp4Box entry)
    {
        return CreateSampleEntry(
            CreateEsdsBox(0x40),
            out entry);
    }

    private static MemoryStream CreateSampleEntry(
        byte[] esds,
        out Mp4Box entry)
    {
        byte[] audioData = new byte[28];

        // Data reference index.
        BinaryPrimitives.WriteUInt16BigEndian(
            audioData.AsSpan(6, 2),
            1);

        // Version zero.
        BinaryPrimitives.WriteUInt16BigEndian(
            audioData.AsSpan(8, 2),
            0);

        // Stereo.
        BinaryPrimitives.WriteUInt16BigEndian(
            audioData.AsSpan(16, 2),
            2);

        // 16-bit samples.
        BinaryPrimitives.WriteUInt16BigEndian(
            audioData.AsSpan(18, 2),
            16);

        // 44100 Hz, 16.16 fixed point.
        BinaryPrimitives.WriteUInt32BigEndian(
            audioData.AsSpan(24, 4),
            44100U << 16);

        byte[] entryBytes = CreateBox(
            "mp4a",
            [.. audioData, .. esds]);

        MemoryStream stream = new(entryBytes);

        entry = new Mp4Box(
            (ulong)entryBytes.Length,
            "mp4a",
            0,
            8);

        return stream;
    }

    private static byte[] CreateEsdsBox(
        byte objectTypeIndication)
    {
        // AAC AudioSpecificConfig:
        // Audio object type = 2 (AAC LC)
        // Sampling frequency index = 4 (44100 Hz)
        // Channel configuration = 2 (stereo)
        byte[] audioSpecificConfig =
        [
            0x12,
            0x10
        ];

        byte[] decoderSpecificInfo = CreateDescriptor(
            0x05,
            audioSpecificConfig);

        // DecoderConfigDescriptor fixed fields.
        byte[] decoderConfig = new byte[13];

        decoderConfig[0] = objectTypeIndication;
        decoderConfig[1] = 0x15;

        byte[] decoderConfigPayload =
        [
            .. decoderConfig,
            .. decoderSpecificInfo
        ];

        byte[] decoderConfigDescriptor = CreateDescriptor(
            0x04,
            decoderConfigPayload);

        // ES_ID and flags.
        byte[] esDescriptorPayload =
        [
            0x00,
            0x01,
            0x00,
            .. decoderConfigDescriptor
        ];

        byte[] esDescriptor = CreateDescriptor(
            0x03,
            esDescriptorPayload);

        // FullBox version and flags followed by ES descriptor.
        byte[] esdsPayload =
        [
            0x00,
            0x00,
            0x00,
            0x00,
            .. esDescriptor
        ];

        return CreateBox("esds", esdsPayload);
    }

    private static byte[] CreateDescriptor(
        byte tag,
        byte[] payload)
    {
        if (payload.Length >= 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload));
        }

        return
        [
            tag,
            (byte)payload.Length,
            .. payload
        ];
    }

    private static byte[] CreateBox(
        string type,
        byte[] payload)
    {
        byte[] result = new byte[8 + payload.Length];

        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(0, 4),
            checked((uint)result.Length));

        System.Text.Encoding.ASCII.GetBytes(
            type,
            result.AsSpan(4, 4));

        payload.CopyTo(result, 8);

        return result;
    }
}