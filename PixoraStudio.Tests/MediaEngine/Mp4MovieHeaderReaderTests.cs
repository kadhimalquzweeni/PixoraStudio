
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Readers.Mp4;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4MovieHeaderReaderTests
{
    private readonly Mp4MovieHeaderReader _reader = new();

    [Fact]
    public void Read_ShouldParseVersionZero()
    {
        byte[] data = CreateVersionZeroData(
            timescale: 1000,
            duration: 5000);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("mvhd", data.Length);

        var result = _reader.Read(stream, box);

        Assert.Equal(1000u, result.Timescale);
        Assert.Equal(5000ul, result.DurationTicks);
        Assert.Equal(5.0, result.DurationSeconds);
    }

    [Fact]
    public void Read_ShouldParseVersionOne()
    {
        byte[] data = CreateVersionOneData(
            timescale: 48000,
            duration: 96000);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("mvhd", data.Length);

        var result = _reader.Read(stream, box);

        Assert.Equal(48000u, result.Timescale);
        Assert.Equal(96000ul, result.DurationTicks);
        Assert.Equal(2.0, result.DurationSeconds);
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = new byte[20];
        data[0] = 2;

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("mvhd", data.Length);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectZeroTimescale()
    {
        byte[] data = CreateVersionZeroData(
            timescale: 0,
            duration: 1000);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("mvhd", data.Length);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectBoxThatIsTooSmall()
    {
        byte[] data = new byte[19];

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("mvhd", data.Length);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectWrongBoxType()
    {
        byte[] data = CreateVersionZeroData(1000, 5000);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("mdhd", data.Length);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRestoreStreamPosition()
    {
        byte[] data = CreateVersionZeroData(1000, 5000);

        using MemoryStream stream = CreateStream(data);
        stream.Position = 3;

        Mp4Box box = CreateBox("mvhd", data.Length);

        _reader.Read(stream, box);

        Assert.Equal(3, stream.Position);
    }

    private static byte[] CreateVersionZeroData(
        uint timescale,
        uint duration)
    {
        byte[] data = new byte[20];

        data[0] = 0;

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            timescale);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(16, 4),
            duration);

        return data;
    }

    private static byte[] CreateVersionOneData(
        uint timescale,
        ulong duration)
    {
        byte[] data = new byte[32];

        data[0] = 1;

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(20, 4),
            timescale);

        BinaryPrimitives.WriteUInt64BigEndian(
            data.AsSpan(24, 8),
            duration);

        return data;
    }

    private static MemoryStream CreateStream(byte[] data)
    {
        MemoryStream stream = new();

        stream.Write(new byte[8]);
        stream.Write(data);
        stream.Position = 0;

        return stream;
    }

    private static Mp4Box CreateBox(string type, int dataSize)
    {
        return new Mp4Box(
            checked((ulong)(8 + dataSize)),
            type,
            0,
            8);
    }
}
