
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4HandlerReaderTests
{
    private readonly Mp4HandlerReader _reader = new();

    [Theory]
    [InlineData("vide", Mp4HandlerType.Video)]
    [InlineData("soun", Mp4HandlerType.Audio)]
    [InlineData("text", Mp4HandlerType.Text)]
    [InlineData("subt", Mp4HandlerType.Subtitle)]
    [InlineData("meta", Mp4HandlerType.Metadata)]
    [InlineData("abcd", Mp4HandlerType.Unknown)]
    public void Read_ShouldIdentifyHandler(
        string handler,
        Mp4HandlerType expected)
    {
        byte[] data = CreateHdlrBox(handler);

        using MemoryStream stream = new(data);

        Mp4HandlerType result = _reader.Read(
            stream,
            CreateBox(data));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Read_ShouldRejectIncorrectBoxType()
    {
        byte[] data = CreateHdlrBox("vide");

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
        byte[] data = new byte[8 + 11];

        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32BigEndian(
                data.AsSpan(0, 4),
                (uint)data.Length);

        Encoding.ASCII.GetBytes("hdlr")
            .CopyTo(data, 4);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldPreserveStreamPosition()
    {
        byte[] data = CreateHdlrBox("vide");

        using MemoryStream stream = new(data);

        stream.Position = 4;

        _reader.Read(stream, CreateBox(data));

        Assert.Equal(4, stream.Position);
    }

    private static Mp4Box CreateBox(byte[] data)
    {
        return new Mp4Box(
            (ulong)data.Length,
            "hdlr",
            0,
            8);
    }

    private static byte[] CreateHdlrBox(string handler)
    {
        byte[] data = new byte[8 + 12];

        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32BigEndian(
                data.AsSpan(0, 4),
                (uint)data.Length);

        Encoding.ASCII.GetBytes("hdlr")
            .CopyTo(data, 4);

        // FullBox version and flags.
        data[8] = 0;

        // Pre-defined field.
        data[12] = 0;

        // Handler type.
        Encoding.ASCII.GetBytes(handler)
            .CopyTo(data, 16);

        return data;
    }
}
