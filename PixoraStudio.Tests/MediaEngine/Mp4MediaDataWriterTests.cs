using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Export;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4MediaDataWriterTests
{
    private readonly Mp4MediaDataWriter _writer = new();

    [Fact]
    public void Write_ShouldCopySelectedSamplesAndReturnAbsoluteOffsets()
    {
        byte[] sourceBytes = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        using MemoryStream source = new(sourceBytes);
        using MemoryStream destination = new();

        Mp4MediaDataWriteResult result = _writer.Write(
            source,
            destination,
            [
                Plan(1, Mp4HandlerType.Video,
                [
                    Sample(1, 2, 3),
                    Sample(2, 10, 2)
                ]),
                Plan(2, Mp4HandlerType.Audio,
                [
                    Sample(1, 20, 4)
                ])
            ]);

        Assert.Equal(0UL, result.BoxOffset);
        Assert.Equal(16UL, result.DataOffset);
        Assert.Equal(9UL, result.DataSize);
        Assert.Equal(new ulong[] { 16, 19 }, result.ChunkOffsetsByTrack[1]);
        Assert.Equal(new ulong[] { 21 }, result.ChunkOffsetsByTrack[2]);

        byte[] output = destination.ToArray();
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(0, 4)));
        Assert.Equal("mdat", Encoding.ASCII.GetString(output, 4, 4));
        Assert.Equal(25UL, BinaryPrimitives.ReadUInt64BigEndian(output.AsSpan(8, 8)));
        Assert.Equal(new byte[] { 2, 3, 4, 10, 11, 20, 21, 22, 23 }, output[16..]);
    }

    [Fact]
    public void Write_ShouldAppendMdatAtCurrentDestinationPosition()
    {
        using MemoryStream source = new(Enumerable.Range(0, 8).Select(value => (byte)value).ToArray());
        using MemoryStream destination = new();
        destination.Write([99, 98]);

        Mp4MediaDataWriteResult result = _writer.Write(
            source,
            destination,
            [Plan(1, Mp4HandlerType.Video, [Sample(1, 1, 2)])]);

        Assert.Equal(2UL, result.BoxOffset);
        Assert.Equal(18UL, result.DataOffset);
        Assert.Equal(2UL, result.DataSize);
        Assert.Equal(new byte[] { 99, 98, 0, 0, 0, 1, (byte)'m', (byte)'d', (byte)'a', (byte)'t' }, destination.ToArray()[..10]);
    }

    [Fact]
    public void Write_ShouldRejectSampleOutsideSourceBounds()
    {
        using MemoryStream source = new(new byte[8]);
        using MemoryStream destination = new();

        Assert.Throws<Mp4ParsingException>(() => _writer.Write(
            source,
            destination,
            [Plan(1, Mp4HandlerType.Video, [Sample(1, 7, 2)])]));
    }

    [Fact]
    public void Write_ShouldRejectDuplicateTrackPlans()
    {
        using MemoryStream source = new(new byte[8]);
        using MemoryStream destination = new();
        Mp4TrimTrackPlan plan = Plan(1, Mp4HandlerType.Video, [Sample(1, 0, 1)]);

        Assert.Throws<ArgumentException>(() => _writer.Write(source, destination, [plan, plan]));
    }

    [Fact]
    public void Write_ShouldStopWhenCancelledBeforeWriting()
    {
        using MemoryStream source = new(new byte[8]);
        using MemoryStream destination = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => _writer.Write(
            source,
            destination,
            [Plan(1, Mp4HandlerType.Video, [Sample(1, 0, 1)])],
            cancellation.Token));

        Assert.Empty(destination.ToArray());
    }

    private static Mp4TrimTrackPlan Plan(
        uint trackId,
        Mp4HandlerType type,
        IEnumerable<Mp4SampleLocation> samples) =>
        new(trackId, type, samples);

    private static Mp4SampleLocation Sample(uint number, ulong offset, uint size) =>
        new(number, 1, offset, size, (ulong)(number - 1) * 100, 100, number == 1)
        {
            SampleDescriptionIndex = 1
        };
}
