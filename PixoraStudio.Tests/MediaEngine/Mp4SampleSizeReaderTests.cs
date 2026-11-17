
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4SampleSizeReaderTests
{
    private readonly Mp4SampleSizeReader _reader = new();

    [Fact]
    public void Read_ShouldParseIndividualSampleSizes()
    {
        byte[] data = CreateStszBox(
            0,
            4,
            1250,
            980,
            1420,
            1100);

        using MemoryStream stream = new(data);

        Mp4SampleSizeTable table =
            _reader.Read(stream, CreateBox(data));

        Assert.Equal(4u, table.SampleCount);
        Assert.Equal(0u, table.DefaultSampleSize);

        Assert.Equal(
            new uint[] { 1250, 980, 1420, 1100 },
            table.SampleSizes);
    }

    [Fact]
    public void Read_ShouldParseFixedSampleSize()
    {
        byte[] data = CreateStszBox(
            1024,
            100,
            []);

        using MemoryStream stream = new(data);

        Mp4SampleSizeTable table =
            _reader.Read(stream, CreateBox(data));

        Assert.Equal(100u, table.SampleCount);
        Assert.Equal(1024u, table.DefaultSampleSize);
        Assert.Empty(table.SampleSizes);

        Assert.Equal(1024u, table.GetSampleSize(0));
        Assert.Equal(1024u, table.GetSampleSize(99));
    }

    [Fact]
    public void Read_ShouldAllowEmptyTable()
    {
        byte[] data = CreateStszBox(0,0);

        using MemoryStream stream = new(data);

        Mp4SampleSizeTable table =
            _reader.Read(stream, CreateBox(data));

        Assert.Equal(0u, table.SampleCount);
        Assert.Empty(table.SampleSizes);
    }

    [Fact]
    public void Read_ShouldGetIndividualSampleSize()
    {
        byte[] data = CreateStszBox(
            0,
            3,
            100,
            200,
            300);

        using MemoryStream stream = new(data);

        Mp4SampleSizeTable table =
            _reader.Read(stream, CreateBox(data));

        Assert.Equal(100u, table.GetSampleSize(0));
        Assert.Equal(200u, table.GetSampleSize(1));
        Assert.Equal(300u, table.GetSampleSize(2));
    }

    [Fact]
    public void Read_ShouldRejectOutOfRangeSampleIndex()
    {
        byte[] data = CreateStszBox(0, 1, 100);

        using MemoryStream stream = new(data);

        Mp4SampleSizeTable table =
            _reader.Read(stream, CreateBox(data));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.GetSampleSize(1));
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = CreateStszBox(0, 100);

        data[8] = 1;

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectMismatchedSampleCount()
    {
        byte[] data = CreateStszBox(0, 100, 200);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(16, 4),
            3);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectUnexpectedIndividualSizes()
    {
        byte[] data = CreateStszBox(
            1024,
            2,
            100,
            200);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectIncorrectBoxType()
    {
        byte[] data = CreateStszBox(0, 100);

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
    public void Read_ShouldPreserveStreamPosition()
    {
        byte[] data = CreateStszBox(0, 2, 100, 200);

        using MemoryStream stream = new(data);

        stream.Position = 4;

        _reader.Read(stream, CreateBox(data));

        Assert.Equal(4, stream.Position);
    }

    private static Mp4Box CreateBox(byte[] data)
    {
        return new Mp4Box(
            (ulong)data.Length,
            "stsz",
            0,
            8);
    }

    private static byte[] CreateStszBox(
        uint defaultSampleSize,
        uint sampleCount,
        params uint[] sampleSizes)
    {
        byte[] data = new byte[20 + sampleSizes.Length * 4];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        Encoding.ASCII.GetBytes("stsz")
            .CopyTo(data, 4);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            defaultSampleSize);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(16, 4),
            sampleCount);

        for (int i = 0; i < sampleSizes.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                data.AsSpan(20 + i * 4, 4),
                sampleSizes[i]);
        }

        return data;
    }
}
