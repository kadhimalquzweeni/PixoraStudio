using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Coordinates POC derivation with the frame output bumping queue. The
/// container layer remains responsible for decode-order sample scheduling and
/// timestamps; this component releases completed surfaces in presentation order.
/// </summary>
public sealed class H264DecodedPictureOutputScheduler
{
    private readonly H264PictureOrderCountCalculator _pocCalculator;
    private readonly H264DecodedPictureOutputQueue _outputQueue;

    public H264DecodedPictureOutputScheduler()
        : this(new H264PictureOrderCountCalculator(), new H264DecodedPictureOutputQueue())
    {
    }

    public H264DecodedPictureOutputScheduler(
        H264PictureOrderCountCalculator pocCalculator,
        H264DecodedPictureOutputQueue outputQueue)
    {
        ArgumentNullException.ThrowIfNull(pocCalculator);
        ArgumentNullException.ThrowIfNull(outputQueue);
        _pocCalculator = pocCalculator;
        _outputQueue = outputQueue;
    }

    public int PendingCount => _outputQueue.PendingCount;

    public IReadOnlyList<H264DecodedPictureOutput> Enqueue(
        H264DecodedPictureSurface surface,
        H264SequenceParameterSet sps,
        H264SliceHeader slice,
        byte nalRefIdc,
        int? maxNumReorderFramesFallback = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(slice);

        IReadOnlyList<H264DecodedPictureOutput> priorOutputs = [];
        if (slice.IdrPicId.HasValue)
        {
            bool noOutput = slice.DecodedReferenceMarking?.NoOutputOfPriorPicsFlag ?? false;
            priorOutputs = _outputQueue.HandleIdr(noOutput);
        }

        int maxNumReorderFrames = ResolveMaxNumReorderFrames(sps, maxNumReorderFramesFallback);
        H264PictureOrderCount poc = _pocCalculator.Calculate(sps, slice, nalRefIdc);
        IReadOnlyList<H264DecodedPictureOutput> ready =
            _outputQueue.Enqueue(surface, poc, maxNumReorderFrames);
        if (priorOutputs.Count == 0)
            return ready;
        return priorOutputs.Concat(ready).ToArray();
    }

    public IReadOnlyList<H264DecodedPictureOutput> Flush() => _outputQueue.Flush();

    public void Reset()
    {
        _pocCalculator.Reset();
        _outputQueue.Reset();
    }

    private static int ResolveMaxNumReorderFrames(
        H264SequenceParameterSet sps,
        int? fallback)
    {
        H264VuiParameters? vui = sps.VuiParameters;
        if (vui is { BitstreamRestrictionFlag: true, MaxNumReorderFrames: uint reorderFrames })
        {
            if (reorderFrames > 16)
                throw new FormatException("VUI max_num_reorder_frames exceeds the supported frame buffer limit.");
            return checked((int)reorderFrames);
        }

        if (fallback is null)
            throw new NotSupportedException(
                "The SPS does not signal max_num_reorder_frames; the container/decoder integration must provide the applicable reorder limit.");
        if (fallback is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(fallback));
        return fallback.Value;
    }
}
