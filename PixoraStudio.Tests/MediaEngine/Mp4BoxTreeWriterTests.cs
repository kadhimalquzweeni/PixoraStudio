using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Processing.Export;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4BoxTreeWriterTests
{
    private readonly Mp4BoxTreeWriter _writer = new();

    [Fact]
    public void Write_ShouldPreserveNestedLeafBoxes()
    {
        using MemoryStream source = CreateSource();
        IReadOnlyList<Mp4Box> boxes = new Mp4Reader().ReadBoxTree(source);
        using MemoryStream destination = new();

        _writer.Write(source, destination, boxes);

        IReadOnlyList<Mp4Box> rewritten = new Mp4Reader().ReadBoxTree(destination);
        Mp4Box moov = Assert.Single(rewritten);
        Assert.Equal("moov", moov.Type);
        Assert.Equal(new[] { "free", "trak" }, moov.Children.Select(box => box.Type));
        Assert.Equal("abcd", ReadPayload(destination, moov.Children[0]));
        Assert.Equal("test", ReadPayload(destination, moov.Children[1].Children[0]));
    }

    [Fact]
    public void Write_ShouldAllowCompleteBoxReplacement()
    {
        using MemoryStream source = CreateSource();
        IReadOnlyList<Mp4Box> boxes = new Mp4Reader().ReadBoxTree(source);
        using MemoryStream destination = new();
        Mp4BoxWriter boxWriter = new();

        _writer.Write(source, destination, boxes, (box, output) =>
        {
            if (box.Type != "free")
                return false;

            boxWriter.WriteBox(output, "free", payload =>
                payload.Write(Encoding.ASCII.GetBytes("new!")));

            return true;
        });

        IReadOnlyList<Mp4Box> rewritten = new Mp4Reader().ReadBoxTree(destination);
        Mp4Box moov = Assert.Single(rewritten);
        Assert.Equal("new!", ReadPayload(destination, moov.Children[0]));
    }

    [Fact]
    public void Write_ShouldRejectSourceAndDestinationBeingTheSameStream()
    {
        using MemoryStream stream = CreateSource();
        IReadOnlyList<Mp4Box> boxes = new Mp4Reader().ReadBoxTree(stream);

        Assert.Throws<ArgumentException>(() => _writer.Write(stream, stream, boxes));
    }

    private static MemoryStream CreateSource()
    {
        Mp4BoxWriter boxWriter = new();
        MemoryStream stream = new();
        boxWriter.WriteBox(stream, "moov", moov =>
        {
            boxWriter.WriteBox(moov, "free", payload =>
                payload.Write(Encoding.ASCII.GetBytes("abcd")));
            boxWriter.WriteBox(moov, "trak", trak =>
                boxWriter.WriteBox(trak, "free", payload =>
                    payload.Write(Encoding.ASCII.GetBytes("test"))));
        });
        stream.Position = 0;
        return stream;
    }

    private static string ReadPayload(Stream stream, Mp4Box box)
    {
        long position = stream.Position;
        try
        {
            stream.Position = box.DataOffset;
            byte[] bytes = new byte[checked((int)box.DataSize)];
            stream.ReadExactly(bytes);
            return Encoding.ASCII.GetString(bytes);
        }
        finally
        {
            stream.Position = position;
        }
    }
}
