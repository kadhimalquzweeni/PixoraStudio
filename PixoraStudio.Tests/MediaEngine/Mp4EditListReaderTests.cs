
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Readers.Mp4;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4EditListReaderTests
{
    private readonly Mp4EditListReader _reader = new();

    [Fact]
    public void Read_ShouldParseVersionZero()
    {
        byte[] data = CreateVersionZeroData(
            segmentDuration: 2000,
            mediaTime: 500);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("elst", data.Length);

        var result = _reader.Read(stream, box);

        Assert.Single(result.Entries);

        Assert.Equal(2000ul, result.Entries[0].SegmentDuration);
        Assert.Equal(500L, result.Entries[0].MediaTime);
        Assert.Equal((short)1, result.Entries[0].MediaRateInteger);
        Assert.Equal((short)0, result.Entries[0].MediaRateFraction);
        Assert.False(result.Entries[0].IsEmptyEdit);
        Assert.True(result.Entries[0].UsesNormalPlaybackRate);
    }

    [Fact]
    public void Read_ShouldParseVersionOne()
    {
        byte[] data = CreateVersionOneData(
            segmentDuration: 5_000_000_000,
            mediaTime: 3_000_000_000);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("elst", data.Length);

        var result = _reader.Read(stream, box);

        Assert.Single(result.Entries);

        Assert.Equal(
            5_000_000_000ul,
            result.Entries[0].SegmentDuration);

        Assert.Equal(
            3_000_000_000L,
            result.Entries[0].MediaTime);
    }

    [Fact]
    public void Read_ShouldPreserveEmptyEdit()
    {
        byte[] data = CreateVersionZeroData(
            segmentDuration: 1000,
            mediaTime: -1);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("elst", data.Length);

        var result = _reader.Read(stream, box);

        Assert.True(result.Entries[0].IsEmptyEdit);
        Assert.Equal(-1, result.Entries[0].MediaTime);
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = new byte[8];
        data[0] = 2;

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("elst", data.Length);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectInvalidMediaTime()
    {
        byte[] data = CreateVersionZeroData(
            segmentDuration: 1000,
            mediaTime: -2);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("elst", data.Length);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedPlaybackRate()
    {
        byte[] data = CreateVersionZeroData(
            segmentDuration: 1000,
            mediaTime: 0,
            rateInteger: 2);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("elst", data.Length);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectIncompleteEntry()
    {
        byte[] data = new byte[19];

        data[0] = 0;

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(4, 4),
            1);

        using MemoryStream stream = CreateStream(data);
        Mp4Box box = CreateBox("elst", data.Length);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRestoreStreamPosition()
    {
        byte[] data = CreateVersionZeroData(1000, 500);

        using MemoryStream stream = CreateStream(data);
        stream.Position = 4;

        Mp4Box box = CreateBox("elst", data.Length);

        _reader.Read(stream, box);

        Assert.Equal(4, stream.Position);
    }

    private static byte[] CreateVersionZeroData(
        uint segmentDuration,
        int mediaTime,
        short rateInteger = 1,
        short rateFraction = 0)
    {
        byte[] data = new byte[20];

        data[0] = 0;

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(4, 4),
            1);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(8, 4),
            segmentDuration);

        BinaryPrimitives.WriteInt32BigEndian(
            data.AsSpan(12, 4),
            mediaTime);

        BinaryPrimitives.WriteInt16BigEndian(
            data.AsSpan(16, 2),
            rateInteger);

        BinaryPrimitives.WriteInt16BigEndian(
            data.AsSpan(18, 2),
            rateFraction);

        return data;
    }

    private static byte[] CreateVersionOneData(
        ulong segmentDuration,
        long mediaTime)
    {
        byte[] data = new byte[28];

        data[0] = 1;

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(4, 4),
            1);

        BinaryPrimitives.WriteUInt64BigEndian(
            data.AsSpan(8, 8),
            segmentDuration);

        BinaryPrimitives.WriteInt64BigEndian(
            data.AsSpan(16, 8),
            mediaTime);

        BinaryPrimitives.WriteInt16BigEndian(
            data.AsSpan(24, 2),
            1);

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
