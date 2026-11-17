
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4CodecConfigurationReaderTests
{
    private readonly Mp4CodecConfigurationReader _reader = new();

    [Fact]
    public void Read_ShouldParseAvcConfiguration()
    {
        byte[] data =
        [
            1,          // Configuration version
            100,        // Profile
            0,           // Compatibility
            40,          // Level
            0xFF,        // NAL length size
            0xE1,        // One SPS
            0, 2,        // SPS length
            0x67, 0x64,  // SPS data
            1,           // One PPS
            0, 2,        // PPS length
            0x68, 0xEE   // PPS data
        ];

        using MemoryStream stream = CreateConfigurationBox(
            "avcC", data, out Mp4Box box);

        Mp4CodecConfiguration result = _reader.Read(
            stream, box);

        Assert.Equal(Mp4VideoCodec.Avc, result.Codec);
        Assert.Equal("avcC", result.BoxType);
        Assert.Equal((byte)1, result.ConfigurationVersion);
        Assert.Equal((byte)100, result.Profile);
        Assert.Equal((byte)0, result.Compatibility);
        Assert.Equal((byte)40, result.Level);
        Assert.Equal(4, result.NaluLengthSize);
        Assert.Equal(data, result.Data.ToArray());
    }

    [Fact]
    public void Read_ShouldParseHevcConfiguration()
    {
        byte[] data = new byte[28];

        data[0] = 1;
        data[1] = 1;
        data[2] = 0x60;
        data[12] = 120;
        data[21] = 3;
        data[22] = 1;

        // One HEVC NAL array.
        data[23] = 0x80 | 32;
        data[24] = 0;
        data[25] = 1;

        // One two-byte NAL unit.
        data[26] = 0;
        data[27] = 2;

        // Add the NAL bytes.
        data = [.. data, 0xAA, 0xBB];

        using MemoryStream stream = CreateConfigurationBox(
            "hvcC", data, out Mp4Box box);

        Mp4CodecConfiguration result = _reader.Read(
            stream, box);

        Assert.Equal(Mp4VideoCodec.Hevc, result.Codec);
        Assert.Equal("hvcC", result.BoxType);
        Assert.Equal((byte)1, result.ConfigurationVersion);
        Assert.Equal(4, result.NaluLengthSize);
        Assert.Equal(data, result.Data.ToArray());
    }

    [Fact]
    public void Read_ShouldRestoreStreamPosition()
    {
        byte[] data =
        [
            1, 100, 0, 40, 0xFF, 0xE0, 0
        ];

        using MemoryStream stream = CreateConfigurationBox(
            "avcC", data, out Mp4Box box);

        stream.Position = 2;

        _reader.Read(stream, box);

        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedConfigurationType()
    {
        using MemoryStream stream = new();

        Mp4Box box = new(
            8, "free", 0, 8);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectIncompleteAvcConfiguration()
    {
        using MemoryStream stream = CreateConfigurationBox(
            "avcC",
            [1, 100, 0, 40],
            out Mp4Box box);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedAvcVersion()
    {
        using MemoryStream stream = CreateConfigurationBox(
            "avcC",
            [2, 100, 0, 40, 0xFF, 0xE0, 0],
            out Mp4Box box);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectIncompleteAvcSps()
    {
        using MemoryStream stream = CreateConfigurationBox(
            "avcC",
            [1, 100, 0, 40, 0xFF, 0xE1, 0, 10],
            out Mp4Box box);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectIncompleteHevcConfiguration()
    {
        using MemoryStream stream = CreateConfigurationBox(
            "hvcC",
            new byte[10],
            out Mp4Box box);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    private static MemoryStream CreateConfigurationBox(
        string type,
        byte[] data,
        out Mp4Box box)
    {
        MemoryStream stream = new();

        long size = data.Length + 8;

        Span<byte> header = stackalloc byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(
            header[..4],
            checked((uint)size));

        System.Text.Encoding.ASCII.GetBytes(type)
            .CopyTo(header[4..]);

        stream.Write(header);
        stream.Write(data);

        box = new Mp4Box(
            checked((ulong)size),
            type,
            0,
            8);

        stream.Position = 0;

        return stream;
    }
}
