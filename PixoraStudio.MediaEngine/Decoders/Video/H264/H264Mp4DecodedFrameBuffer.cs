using System.Threading.Channels;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// A bounded, single-producer/single-consumer queue for decoded MP4 pictures.
/// Writes wait when the consumer falls behind, limiting decoded-frame memory.
/// </summary>
public sealed class H264Mp4DecodedFrameBuffer
{
    private readonly Channel<H264Mp4DecodedVideoFrame> _channel;

    public H264Mp4DecodedFrameBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _channel = Channel.CreateBounded<H264Mp4DecodedVideoFrame>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
    }

    public ValueTask WriteAsync(
        H264Mp4DecodedVideoFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return _channel.Writer.WriteAsync(frame, cancellationToken);
    }

    public IAsyncEnumerable<H264Mp4DecodedVideoFrame> ReadAllAsync(
        CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Completes the queue. Supplying an exception makes the reader observe
    /// the producer failure after all already-buffered frames are consumed.
    /// </summary>
    public bool Complete(Exception? exception = null) =>
        _channel.Writer.TryComplete(exception);
}
