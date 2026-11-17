
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4TrackHeaderReaderTests
{
    private readonly Mp4TrackHeaderReader _reader = new();

    [Fact]
    public void Read_ShouldParseVersionZeroHeader()
    {
        byte[] data = CreateTkhdBox(
            version: 0,
            trackId: 1,
            width: 1920,
            height: 1080);

        using MemoryStream stream = new(data);

        Mp4Box box = CreateBox(data);

        Mp4Track track = _reader.Read(stream, box);

        Assert.Equal(1u, track.TrackId);
        Assert.Equal(1920, track.Width);
        Assert.Equal(1080, track.Height);
    }

    [Fact]
    public void Read_ShouldParseVersionOneHeader()
    {
        byte[] data = CreateTkhdBox(
            version: 1,
            trackId: 5,
            width: 3840,
            height: 2160);

        using MemoryStream stream = new(data);

        Mp4Box box = CreateBox(data);

        Mp4Track track = _reader.Read(stream, box);

        Assert.Equal(5u, track.TrackId);
        Assert.Equal(3840, track.Width);
        Assert.Equal(2160, track.Height);
    }

    [Fact]
    public void Read_ShouldConvertFixedPointDimensions()
    {
        byte[] data = CreateTkhdBox(
            version: 0,
            trackId: 2,
            width: 1920.5,
            height: 1080.25);

        using MemoryStream stream = new(data);

        Mp4Box box = CreateBox(data);

        Mp4Track track = _reader.Read(stream, box);

        Assert.Equal(1920.5, track.Width);
        Assert.Equal(1080.25, track.Height);
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = CreateTkhdBox(
            version: 2,
            trackId: 1,
            width: 1920,
            height: 1080);

        using MemoryStream stream = new(data);

        Mp4Box box = CreateBox(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectIncorrectBoxType()
    {
        byte[] data = CreateTkhdBox(
            version: 0,
            trackId: 1,
            width: 1920,
            height: 1080);

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
        byte[] data = new byte[8 + 83];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        System.Text.Encoding.ASCII.GetBytes("tkhd")
            .CopyTo(data, 4);

        data[8] = 0;

        using MemoryStream stream = new(data);

        Mp4Box box = CreateBox(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldPreserveStreamPosition()
    {
        byte[] data = CreateTkhdBox(
            version: 0,
            trackId: 1,
            width: 1920,
            height: 1080);

        using MemoryStream stream = new(data);

        stream.Position = 4;

        Mp4Box box = CreateBox(data);

        _reader.Read(stream, box);

        Assert.Equal(4, stream.Position);
    }

    private static Mp4Box CreateBox(byte[] data)
    {
        return new Mp4Box(
            (ulong)data.Length,
            "tkhd",
            0,
            8);
    }

    private static byte[] CreateTkhdBox(
        byte version,
        uint trackId,
        double width,
        double height)
    {
        int payloadSize = version switch
        {
            0 => 84,
            1 => 96,
            _ => 84
        };

        int trackIdOffset = version == 1 ? 20 : 12;
        int dimensionsOffset = version == 1 ? 88 : 76;

        byte[] data = new byte[8 + payloadSize];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        System.Text.Encoding.ASCII.GetBytes("tkhd")
            .CopyTo(data, 4);

        Span<byte> payload = data.AsSpan(8);

        payload[0] = version;

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.Slice(trackIdOffset, 4),
            trackId);

        uint rawWidth = checked((uint)(width * 65536));
        uint rawHeight = checked((uint)(height * 65536));

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.Slice(dimensionsOffset, 4),
            rawWidth);

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.Slice(dimensionsOffset + 4, 4),
            rawHeight);

        return data;
    }
}
