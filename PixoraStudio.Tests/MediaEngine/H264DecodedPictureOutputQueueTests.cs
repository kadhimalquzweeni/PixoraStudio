using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DecodedPictureOutputQueueTests
{
    [Fact]
    public void Enqueue_ShouldHoldFramesUntilReorderLimitIsExceeded()
    {
        H264DecodedPictureOutputQueue queue = new();
        H264DecodedPictureSurface first = Surface(10);
        H264DecodedPictureSurface second = Surface(20);

        Assert.Empty(queue.Enqueue(first, Poc(4), 2));
        Assert.Empty(queue.Enqueue(second, Poc(2), 2));
        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public void Enqueue_ShouldReleaseLowestPocWhenReorderLimitIsExceeded()
    {
        H264DecodedPictureOutputQueue queue = new();
        H264DecodedPictureSurface poc4 = Surface(40);
        H264DecodedPictureSurface poc2 = Surface(20);
        H264DecodedPictureSurface poc6 = Surface(60);

        queue.Enqueue(poc4, Poc(4), 2);
        queue.Enqueue(poc2, Poc(2), 2);
        IReadOnlyList<H264DecodedPictureOutput> ready =
            queue.Enqueue(poc6, Poc(6), 2);

        H264DecodedPictureOutput output = Assert.Single(ready);
        Assert.Same(poc2, output.Surface);
        Assert.Equal(2, output.PictureOrderCount);
        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public void Flush_ShouldReturnAllPendingFramesInAscendingPocOrder()
    {
        H264DecodedPictureOutputQueue queue = new();
        H264DecodedPictureSurface[] surfaces =
        [
            Surface(40), Surface(10), Surface(30), Surface(20)
        ];
        int[] pocs = [4, 1, 3, 2];
        for (int i = 0; i < surfaces.Length; i++)
            queue.Enqueue(surfaces[i], Poc(pocs[i]), 4);

        IReadOnlyList<H264DecodedPictureOutput> output = queue.Flush();

        Assert.Equal(new[] { 1, 2, 3, 4 }, output.Select(item => item.PictureOrderCount));
        Assert.Equal(new[] { 1, 2, 3, 4 },
            output.Select(item => item.Surface.GetLumaSample(0, 0) / 10));
        Assert.Empty(queue.Flush());
    }

    [Fact]
    public void Flush_ShouldPreserveDecodeOrderForEqualPocValues()
    {
        H264DecodedPictureOutputQueue queue = new();
        H264DecodedPictureSurface first = Surface(10);
        H264DecodedPictureSurface second = Surface(20);
        queue.Enqueue(first, Poc(3), 2);
        queue.Enqueue(second, Poc(3), 2);

        IReadOnlyList<H264DecodedPictureOutput> output = queue.Flush();

        Assert.Same(first, output[0].Surface);
        Assert.Same(second, output[1].Surface);
    }

    [Fact]
    public void Enqueue_WithZeroReorderLimit_ShouldReleaseEveryFrameImmediately()
    {
        H264DecodedPictureOutputQueue queue = new();
        H264DecodedPictureSurface surface = Surface(80);

        IReadOnlyList<H264DecodedPictureOutput> output =
            queue.Enqueue(surface, Poc(5), 0);

        Assert.Same(surface, Assert.Single(output).Surface);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void HandleIdr_ShouldFlushOrDiscardPriorPictures()
    {
        H264DecodedPictureOutputQueue queue = new();
        H264DecodedPictureSurface first = Surface(10);
        H264DecodedPictureSurface second = Surface(20);
        queue.Enqueue(first, Poc(4), 4);
        queue.Enqueue(second, Poc(2), 4);

        IReadOnlyList<H264DecodedPictureOutput> flushed =
            queue.HandleIdr(false);

        Assert.Equal(new[] { 2, 4 }, flushed.Select(item => item.PictureOrderCount));
        Assert.Empty(queue.HandleIdr(true));
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void HandleIdr_WithNoOutputOfPriorPicsTrue_ShouldDiscardPendingFrames()
    {
        H264DecodedPictureOutputQueue queue = new();
        queue.Enqueue(Surface(10), Poc(4), 4);
        queue.Enqueue(Surface(20), Poc(2), 4);

        IReadOnlyList<H264DecodedPictureOutput> output = queue.HandleIdr(true);

        Assert.Empty(output);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Reset_ShouldClearQueueAndSequence()
    {
        H264DecodedPictureOutputQueue queue = new();
        queue.Enqueue(Surface(10), Poc(2), 3);
        queue.Reset();

        Assert.Equal(0, queue.PendingCount);
        Assert.Empty(queue.Flush());
    }

    [Fact]
    public void Enqueue_ShouldRejectIncompletePictureSurface()
    {
        H264SequenceParameterSet sps = new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

        Assert.Throws<InvalidOperationException>(() =>
            new H264DecodedPictureOutputQueue().Enqueue(
                new H264DecodedPictureSurface(sps), Poc(0), 0));
    }

    [Fact]
    public void Enqueue_ShouldRejectInvalidReorderLimitAndNullInputs()
    {
        H264DecodedPictureOutputQueue queue = new();
        Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!, Poc(0), 0));
        Assert.Throws<ArgumentNullException>(() => queue.Enqueue(Surface(1), null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            queue.Enqueue(Surface(1), Poc(0), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            queue.Enqueue(Surface(1), Poc(0), 17));
    }

    private static H264PictureOrderCount Poc(int value) =>
        new(value, value, value, 0, true, true);

    private static H264DecodedPictureSurface Surface(byte value)
    {
        H264SequenceParameterSet sps = new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);
        H264DecodedPictureSurface surface = new(sps);
        surface.WriteMacroblock(0, 0, 0, false,
            Enumerable.Repeat(value, 256).ToArray(),
            Enumerable.Repeat(value, 64).ToArray(),
            Enumerable.Repeat(value, 64).ToArray());
        return surface;
    }
}
