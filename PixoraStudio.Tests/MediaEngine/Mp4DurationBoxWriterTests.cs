using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Processing.Export;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4DurationBoxWriterTests
{
    private readonly Mp4DurationBoxWriter _writer = new();

    [Theory]
    [InlineData("mvhd", 0, 16, 20)]
    [InlineData("mdhd", 0, 16, 20)]
    [InlineData("tkhd", 0, 20, 84)]
    public void Write_ShouldPatchVersionZeroDuration(
        string type,
        byte version,
        int durationOffset,
        int payloadSize)
    {
        using MemoryStream source = CreateBox(type, version, payloadSize);
        Mp4Box box = Assert.Single(new Mp4Reader().ReadBoxTree(source));
        using MemoryStream destination = new();

        _writer.Write(source, destination, box, 123456);

        byte[] output = destination.ToArray();
        Assert.Equal(123456u, BinaryPrimitives.ReadUInt32BigEndian(
            output.AsSpan(checked((int)box.HeaderSize + durationOffset), 4)));
    }

    [Theory]
    [InlineData("mvhd", 24, 32)]
    [InlineData("mdhd", 24, 32)]
    [InlineData("tkhd", 28, 96)]
    public void Write_ShouldPatchVersionOneDuration(
        string type,
        int durationOffset,
        int payloadSize)
    {
        using MemoryStream source = CreateBox(type, 1, payloadSize);
        Mp4Box box = Assert.Single(new Mp4Reader().ReadBoxTree(source));
        using MemoryStream destination = new();

        _writer.Write(source, destination, box, (ulong)uint.MaxValue + 123);

        byte[] output = destination.ToArray();
        Assert.Equal((ulong)uint.MaxValue + 123, BinaryPrimitives.ReadUInt64BigEndian(
            output.AsSpan(checked((int)box.HeaderSize + durationOffset), 8)));
    }

    [Fact]
    public void Write_ShouldRejectVersionZeroOverflow()
    {
        using MemoryStream source = CreateBox("mvhd", 0, 20);
        Mp4Box box = Assert.Single(new Mp4Reader().ReadBoxTree(source));

        Assert.Throws<NotSupportedException>(() =>
            _writer.Write(source, new MemoryStream(), box, (ulong)uint.MaxValue + 1));
    }

    [Fact]
    public void Write_ShouldRejectUnsupportedBoxType()
    {
        using MemoryStream source = CreateBox("free", 0, 20);
        Mp4Box box = Assert.Single(new Mp4Reader().ReadBoxTree(source));

        Assert.Throws<Mp4ParsingException>(() =>
            _writer.Write(source, new MemoryStream(), box, 1));
    }

    private static MemoryStream CreateBox(string type, byte version, int payloadSize)
    {
        Mp4BoxWriter writer = new();
        MemoryStream stream = new();
        byte[] payload = new byte[payloadSize];
        payload[0] = version;
        writer.WriteBox(stream, type, output => output.Write(payload));
        stream.Position = 0;
        return stream;
    }
}
