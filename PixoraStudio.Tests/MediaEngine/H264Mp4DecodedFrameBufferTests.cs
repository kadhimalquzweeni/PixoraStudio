using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4DecodedFrameBufferTests
{
    [Fact]
    public async Task WriteAsync_WhenCapacityIsReached_ShouldWaitUntilAFrameIsRead()
    {
        H264Mp4DecodedFrameBuffer buffer = new(1);
        H264Mp4DecodedVideoFrame first = CreateFrame(1);
        H264Mp4DecodedVideoFrame second = CreateFrame(2);

        await buffer.WriteAsync(first);
        Task pendingWrite = buffer.WriteAsync(second).AsTask();

        Assert.False(pendingWrite.IsCompleted);

        await using IAsyncEnumerator<H264Mp4DecodedVideoFrame> reader =
            buffer.ReadAllAsync().GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        Assert.Same(first, reader.Current);

        await pendingWrite;

        Assert.True(await reader.MoveNextAsync());
        Assert.Same(second, reader.Current);

        buffer.Complete();
        Assert.False(await reader.MoveNextAsync());
    }

    [Fact]
    public async Task ReadAllAsync_ShouldPreserveDecodePresentationQueueOrder()
    {
        H264Mp4DecodedFrameBuffer buffer = new(3);
        H264Mp4DecodedVideoFrame[] expected =
        [
            CreateFrame(3),
            CreateFrame(1),
            CreateFrame(2)
        ];

        foreach (H264Mp4DecodedVideoFrame frame in expected)
            await buffer.WriteAsync(frame);

        buffer.Complete();

        List<H264Mp4DecodedVideoFrame> actual = [];
        await foreach (H264Mp4DecodedVideoFrame frame in buffer.ReadAllAsync())
            actual.Add(frame);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task WriteAsync_WhenBlocked_ShouldObserveCancellation()
    {
        H264Mp4DecodedFrameBuffer buffer = new(1);
        await buffer.WriteAsync(CreateFrame(1));

        using CancellationTokenSource cancellation = new();
        Task pendingWrite = buffer.WriteAsync(
            CreateFrame(2), cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pendingWrite);
    }

    [Fact]
    public async Task ReadAllAsync_WhenProducerCompletesWithError_ShouldThrowAfterBufferedFrames()
    {
        H264Mp4DecodedFrameBuffer buffer = new(2);
        H264Mp4DecodedVideoFrame frame = CreateFrame(1);
        InvalidDataException failure = new("Synthetic decoder failure.");

        await buffer.WriteAsync(frame);
        buffer.Complete(failure);

        await using IAsyncEnumerator<H264Mp4DecodedVideoFrame> reader =
            buffer.ReadAllAsync().GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        Assert.Same(frame, reader.Current);

        InvalidDataException actual = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await reader.MoveNextAsync());

        Assert.Same(failure, actual);
    }

    [Fact]
    public void Constructor_WithNonPositiveCapacity_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new H264Mp4DecodedFrameBuffer(0));
    }

    private static H264Mp4DecodedVideoFrame CreateFrame(uint sampleNumber)
    {
        H264Mp4VideoSample sample = new(
            1,
            sampleNumber,
            0,
            0,
            1,
            1,
            1,
            1,
            0,
            []);
        return new H264Mp4DecodedVideoFrame(
            null!,
            checked((int)sampleNumber),
            sample);
    }
}
