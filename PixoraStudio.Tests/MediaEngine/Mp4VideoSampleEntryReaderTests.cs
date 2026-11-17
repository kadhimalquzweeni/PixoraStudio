
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4VideoSampleEntryReaderTests
{
    private readonly Mp4VideoSampleEntryReader _reader = new();

    [Fact]
    public void Read_ShouldParseAvcVideoSampleEntry()
    {
        using MemoryStream stream = CreateSampleEntry(
            "avc1",
            ("avcC", CreateAvcConfiguration()),
            out Mp4Box entry);

        Mp4VideoSampleEntry result = _reader.Read(
            stream, entry);

        Assert.Equal("avc1", result.Format);
        Assert.Equal((ushort)1, result.DataReferenceIndex);
        Assert.Equal((ushort)1920, result.Width);
        Assert.Equal((ushort)1080, result.Height);
        Assert.Equal(72.0, result.HorizontalResolution);
        Assert.Equal(72.0, result.VerticalResolution);
        Assert.Equal((ushort)1, result.FrameCount);
        Assert.Equal("Pixora", result.CompressorName);
        Assert.Equal((ushort)24, result.Depth);
        Assert.Equal(Mp4VideoCodec.Avc, result.Codec);
        Assert.NotNull(result.CodecConfiguration);
    }

    [Fact]
    public void Read_ShouldParseHevcVideoSampleEntry()
    {
        using MemoryStream stream = CreateSampleEntry(
            "hvc1",
            ("hvcC", CreateHevcConfiguration()),
            out Mp4Box entry);

        Mp4VideoSampleEntry result = _reader.Read(
            stream, entry);

        Assert.Equal(Mp4VideoCodec.Hevc, result.Codec);
        Assert.Equal("hvcC", result.CodecConfiguration!.BoxType);
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedSampleEntry()
    {
        using MemoryStream stream = new();

        Mp4Box entry = new(
            8, "vp09", 0, 8);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, entry));
    }

    [Fact]
    public void Read_ShouldRejectMissingConfiguration()
    {
        using MemoryStream stream = CreateSampleEntry(
            "avc1",
            null,
            out Mp4Box entry);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, entry));
    }

    [Fact]
    public void Read_ShouldRejectWrongConfigurationType()
    {
        using MemoryStream stream = CreateSampleEntry(
            "avc1",
            ("hvcC", CreateHevcConfiguration()),
            out Mp4Box entry);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, entry));
    }

    private static MemoryStream CreateSampleEntry(
        string format,
        (string Type, byte[] Data)? configuration,
        out Mp4Box entry)
    {
        MemoryStream stream = new();

        // Reserve the sample-entry box header.
        stream.Write(new byte[8]);

        byte[] visualData = new byte[78];

        BinaryPrimitives.WriteUInt16BigEndian(
            visualData.AsSpan(6, 2), 1);

        BinaryPrimitives.WriteUInt16BigEndian(
            visualData.AsSpan(24, 2), 1920);

        BinaryPrimitives.WriteUInt16BigEndian(
            visualData.AsSpan(26, 2), 1080);

        BinaryPrimitives.WriteUInt32BigEndian(
            visualData.AsSpan(28, 4), 72U << 16);

        BinaryPrimitives.WriteUInt32BigEndian(
            visualData.AsSpan(32, 4), 72U << 16);

        BinaryPrimitives.WriteUInt16BigEndian(
            visualData.AsSpan(40, 2), 1);

        byte[] compressor = Encoding.ASCII.GetBytes("Pixora");

        visualData[42] = checked((byte)compressor.Length);
        compressor.CopyTo(visualData, 43);

        BinaryPrimitives.WriteUInt16BigEndian(
            visualData.AsSpan(74, 2), 24);

        BinaryPrimitives.WriteUInt16BigEndian(
            visualData.AsSpan(76, 2), 0xFFFF);

        stream.Write(visualData);

        if (configuration is { } config)
        {
            WriteUInt32(
                stream,
                checked((uint)(config.Data.Length + 8)));

            stream.Write(Encoding.ASCII.GetBytes(config.Type));
            stream.Write(config.Data);
        }

        long size = stream.Length;

        stream.Position = 0;

        WriteUInt32(stream, checked((uint)size));
        stream.Write(Encoding.ASCII.GetBytes(format));

        entry = new Mp4Box(
            checked((ulong)size),
            format,
            0,
            8);

        stream.Position = 0;

        return stream;
    }

    private static byte[] CreateAvcConfiguration()
    {
        return
        [
            1, 100, 0, 40, 0xFF,
            0xE1, 0, 2, 0x67, 0x64,
            1, 0, 2, 0x68, 0xEE
        ];
    }

    private static byte[] CreateHevcConfiguration()
    {
        byte[] data = new byte[30];

        data[0] = 1;
        data[1] = 1;
        data[12] = 120;
        data[21] = 3;
        data[22] = 1;
        data[23] = 0xA0;
        data[24] = 0;
        data[25] = 1;
        data[26] = 0;
        data[27] = 2;
        data[28] = 0xAA;
        data[29] = 0xBB;

        return data;
    }

    private static void WriteUInt32(
        Stream stream,
        uint value)
    {
        Span<byte> buffer = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(
            buffer, value);

        stream.Write(buffer);
    }
}
