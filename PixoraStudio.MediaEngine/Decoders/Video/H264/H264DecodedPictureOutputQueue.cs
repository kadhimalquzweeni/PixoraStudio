using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Buffers decoded frame surfaces until their presentation order is known
/// from picture order count. It implements a simple frame-only output bumping
/// queue; timestamps and field-pair output are handled by the container layer.
/// </summary>
public sealed class H264DecodedPictureOutputQueue
{
    private readonly List<QueuedPicture> _pending = [];
    private long _nextSequence;

    public int PendingCount => _pending.Count;

    /// <summary>
    /// Queues a decoded frame and releases the lowest-POC frame whenever the
    /// pending count exceeds max_num_reorder_frames.
    /// </summary>
    public IReadOnlyList<H264DecodedPictureOutput> Enqueue(
        H264DecodedPictureSurface surface,
        H264PictureOrderCount pictureOrderCount,
        int maxNumReorderFrames)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(pictureOrderCount);
        if (maxNumReorderFrames is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maxNumReorderFrames));

        _pending.Add(new QueuedPicture(
            surface, pictureOrderCount.PictureOrderCount, _nextSequence++));

        List<H264DecodedPictureOutput> ready = [];
        while (_pending.Count > maxNumReorderFrames)
            ready.Add(RemoveLowestPoc());
        return ready;
    }

    /// <summary>Outputs all remaining frames in ascending POC order.</summary>
    public IReadOnlyList<H264DecodedPictureOutput> Flush()
    {
        List<H264DecodedPictureOutput> result = new(_pending.Count);
        while (_pending.Count > 0)
            result.Add(RemoveLowestPoc());
        return result;
    }

    /// <summary>
    /// Applies IDR no_output_of_prior_pics_flag. When true, pending prior
    /// pictures are discarded; otherwise they are returned in display order.
    /// </summary>
    public IReadOnlyList<H264DecodedPictureOutput> HandleIdr(
        bool noOutputOfPriorPicsFlag)
    {
        if (noOutputOfPriorPicsFlag)
        {
            _pending.Clear();
            return [];
        }
        return Flush();
    }

    public void Reset()
    {
        _pending.Clear();
        _nextSequence = 0;
    }

    private H264DecodedPictureOutput RemoveLowestPoc()
    {
        int selectedIndex = 0;
        for (int i = 1; i < _pending.Count; i++)
        {
            QueuedPicture candidate = _pending[i];
            QueuedPicture selected = _pending[selectedIndex];
            if (candidate.PictureOrderCount < selected.PictureOrderCount ||
                candidate.PictureOrderCount == selected.PictureOrderCount &&
                candidate.Sequence < selected.Sequence)
                selectedIndex = i;
        }

        QueuedPicture picture = _pending[selectedIndex];
        _pending.RemoveAt(selectedIndex);
        return new H264DecodedPictureOutput(picture.Surface, picture.PictureOrderCount);
    }

    private sealed record QueuedPicture(
        H264DecodedPictureSurface Surface,
        int PictureOrderCount,
        long Sequence);
}

/// <summary>One decoded frame released in presentation order.</summary>
public sealed record H264DecodedPictureOutput(
    H264DecodedPictureSurface Surface,
    int PictureOrderCount);
