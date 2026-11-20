using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Export;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4EditListBoxWriterTests
{
    [Fact]
    public void Write_ShouldCreateReadableVersionOneEditList()
    {
        Mp4EditListBoxWriter writer = new();
        using MemoryStream output = new();

        writer.Write(output,
        [
            new Mp4EditListEntry(500, -1, 1, 0),
            new Mp4EditListEntry(2500, 120, 1, 0)
        ]);

        output.Position = 0;
        Mp4Box edts = Assert.Single(new Mp4Reader().ReadBoxTree(output));
        Mp4Box elst = Assert.Single(edts.Children);
        Mp4EditList editList = new Mp4EditListReader().Read(output, elst);

        Assert.Equal(
            new[]
            {
                new Mp4EditListEntry(500, -1, 1, 0),
                new Mp4EditListEntry(2500, 120, 1, 0)
            },
            editList.Entries);
    }

    [Fact]
    public void Write_ShouldRejectEmptyEditList()
    {
        Assert.Throws<ArgumentException>(() =>
            new Mp4EditListBoxWriter().Write(new MemoryStream(), []));
    }

    [Fact]
    public void Write_ShouldRejectUnsupportedPlaybackRate()
    {
        Assert.Throws<PixoraStudio.MediaEngine.Exceptions.Mp4ParsingException>(() =>
            new Mp4EditListBoxWriter().Write(
                new MemoryStream(),
                [new Mp4EditListEntry(1000, 0, 2, 0)]));
    }
}
