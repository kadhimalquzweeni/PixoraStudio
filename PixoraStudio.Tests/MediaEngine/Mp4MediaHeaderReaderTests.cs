
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Readers.Mp4;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4MediaHeaderReaderTests
{
    private readonly Mp4MediaHeaderReader _reader = new();

    [Fact]
    public void Read_ShouldParseVersionZeroHeader()
    {
        byte[] data = CreateMdhdBox(
            version: 0,
            timescale: 1000,
            duration: 120000);

        using MemoryStream stream = new(data);

        var result = _reader.Read(stream, CreateBox(data));

        Assert.Equal(1000u, result.Timescale);
        Assert.Equal(120000ul, result.DurationTicks);
    }

    [Fact]
    public void Read_ShouldParseVersionOneHeader()
    {
        byte[] data = CreateMdhdBox(
            version: 1,
            timescale: 48000,
            duration: 960000);

        using MemoryStream stream = new(data);

        var result = _reader.Read(stream, CreateBox(data));

        Assert.Equal(48000u, result.Timescale);
        Assert.Equal(960000ul, result.DurationTicks);
    }

    [Fact]
    public void Read_ShouldSupportLargeVersionOneDuration()
    {
        ulong duration = 5_000_000_000;

        byte[] data = CreateMdhdBox(
            version: 1,
            timescale: 1000,
            duration: duration);

        using MemoryStream stream = new(data);

        var result = _reader.Read(stream, CreateBox(data));

        Assert.Equal(duration, result.DurationTicks);
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = CreateMdhdBox(
            version: 2,
            timescale: 1000,
            duration: 1000);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectZeroTimescale()
    {
        byte[] data = CreateMdhdBox(
            version: 0,
            timescale: 0,
            duration: 1000);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectIncorrectBoxType()
    {
        byte[] data = CreateMdhdBox(
            version: 0,
            timescale: 1000,
            duration: 1000);

        using MemoryStream stream = new(data);

        Mp4Box box = new(
            (ulong)data.Length,
            "free",
            0,
            8);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectIncompleteHeader()
    {
        byte[] data = new byte[8 + 19];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        Encoding.ASCII.GetBytes("mdhd")
            .CopyTo(data, 4);

        data[8] = 0;

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldPreserveStreamPosition()
    {
        byte[] data = CreateMdhdBox(
            version: 0,
            timescale: 1000,
            duration: 120000);

        using MemoryStream stream = new(data);

        stream.Position = 4;

        _reader.Read(stream, CreateBox(data));

        Assert.Equal(4, stream.Position);
    }

    private static Mp4Box CreateBox(byte[] data)
    {
        return new Mp4Box(
            (ulong)data.Length,
            "mdhd",
            0,
            8);
    }

    private static byte[] CreateMdhdBox(
        byte version,
        uint timescale,
        ulong duration)
    {
        int payloadSize = version switch
        {
            0 => 20,
            1 => 32,
            _ => 32
        };

        int timescaleOffset = version == 1 ? 20 : 12;
        int durationOffset = version == 1 ? 24 : 16;

        byte[] data = new byte[8 + payloadSize];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        Encoding.ASCII.GetBytes("mdhd")
            .CopyTo(data, 4);

        Span<byte> payload = data.AsSpan(8);

        payload[0] = version;

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.Slice(timescaleOffset, 4),
            timescale);

        if (version == 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                payload.Slice(durationOffset, 4),
                checked((uint)duration));
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                payload.Slice(durationOffset, 8),
                duration);
        }

        return data;
    }
}
