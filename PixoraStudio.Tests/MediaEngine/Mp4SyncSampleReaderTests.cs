using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4SyncSampleReaderTests
{
    private readonly Mp4SyncSampleReader _reader = new();

    [Fact]
    public void Read_ShouldParseSyncSamples()
    {
        byte[] data = CreateStssBox(1, 5, 10, 20);

        using MemoryStream stream = new(data);

        Mp4SyncSampleTable table =
            _reader.Read(stream, CreateBox(data));

        Assert.Equal(4u, table.SampleCount);

        Assert.Equal(
            new uint[] { 1, 5, 10, 20 },
            table.SampleNumbers);
    }

    [Fact]
    public void Read_ShouldIdentifySyncSamples()
    {
        byte[] data = CreateStssBox(1, 5, 10);

        using MemoryStream stream = new(data);

        Mp4SyncSampleTable table =
            _reader.Read(stream, CreateBox(data));

        Assert.True(table.IsSyncSample(1));
        Assert.True(table.IsSyncSample(5));
        Assert.True(table.IsSyncSample(10));

        Assert.False(table.IsSyncSample(2));
        Assert.False(table.IsSyncSample(6));
    }

    [Fact]
    public void Read_ShouldReturnFalseForZeroSampleNumber()
    {
        byte[] data = CreateStssBox(1, 5);

        using MemoryStream stream = new(data);

        Mp4SyncSampleTable table =
            _reader.Read(stream, CreateBox(data));

        Assert.False(table.IsSyncSample(0));
    }

    [Fact]
    public void Read_ShouldAllowEmptyTable()
    {
        byte[] data = CreateStssBox();

        using MemoryStream stream = new(data);

        Mp4SyncSampleTable table =
            _reader.Read(stream, CreateBox(data));

        Assert.Equal(0u, table.SampleCount);
        Assert.Empty(table.SampleNumbers);
        Assert.False(table.IsSyncSample(1));
    }

    [Fact]
    public void Read_ShouldRejectZeroSampleNumber()
    {
        byte[] data = CreateStssBox(0, 5);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectDuplicateSampleNumbers()
    {
        byte[] data = CreateStssBox(1, 5, 5);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectUnorderedSampleNumbers()
    {
        byte[] data = CreateStssBox(1, 10, 5);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = CreateStssBox(1, 5);

        data[8] = 1;

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectMismatchedSampleCount()
    {
        byte[] data = CreateStssBox(1, 5);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            3);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectIncorrectBoxType()
    {
        byte[] data = CreateStssBox(1);

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
        byte[] data = CreateStssBox(1, 5, 10);

        using MemoryStream stream = new(data);

        stream.Position = 4;

        _reader.Read(stream, CreateBox(data));

        Assert.Equal(4, stream.Position);
    }

    [Fact]
    public void Model_ShouldRejectInvalidSampleNumbers()
    {
        Assert.Throws<ArgumentException>(
            () => new Mp4SyncSampleTable([1, 0, 5]));

        Assert.Throws<ArgumentException>(
            () => new Mp4SyncSampleTable([1, 5, 5]));

        Assert.Throws<ArgumentException>(
            () => new Mp4SyncSampleTable([1, 10, 5]));
    }

    private static Mp4Box CreateBox(byte[] data)
    {
        return new Mp4Box(
            (ulong)data.Length,
            "stss",
            0,
            8);
    }

    private static byte[] CreateStssBox(
        params uint[] sampleNumbers)
    {
        byte[] data = new byte[16 + sampleNumbers.Length * 4];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        Encoding.ASCII.GetBytes("stss")
            .CopyTo(data, 4);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            (uint)sampleNumbers.Length);

        for (int i = 0; i < sampleNumbers.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                data.AsSpan(16 + i * 4, 4),
                sampleNumbers[i]);
        }

        return data;
    }
}