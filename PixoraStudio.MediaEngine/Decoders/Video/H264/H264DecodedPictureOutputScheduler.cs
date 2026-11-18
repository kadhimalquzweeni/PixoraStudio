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
        if (!surface.IsComplete)
            throw new InvalidOperationException("A picture must be fully reconstructed before it enters the output queue.");
        if (nalRefIdc > 3)
            throw new ArgumentOutOfRangeException(nameof(nalRefIdc));
        if (slice.IdrPicId.HasValue &&
            (nalRefIdc == 0 || slice.SliceType is not (H264SliceType.I or H264SliceType.Si)))
            throw new FormatException("An IDR picture must use a reference I/SI slice.");
        if (!sps.FrameMbsOnlyFlag || sps.MbAdaptiveFrameFieldFlag || slice.FieldPicFlag)
            throw new NotSupportedException("Decoded picture output scheduling currently supports frame pictures only.");
        if (surface.MacroblockWidth != checked((int)(sps.PicWidthInMbsMinus1 + 1)) ||
            surface.MacroblockHeight != checked((int)(sps.PicHeightInMapUnitsMinus1 + 1)) ||
            surface.ChromaFormatIdc != sps.ChromaFormatIdc)
            throw new ArgumentException("The SPS geometry and chroma format must match the decoded surface.", nameof(sps));
        if (slice.IdrPicId.HasValue && slice.DecodedReferenceMarking is null)
            throw new FormatException("An IDR slice must include decoded reference marking flags.");

        int maxNumReorderFrames = ResolveMaxNumReorderFrames(sps, maxNumReorderFramesFallback);
        H264PictureOrderCount poc = _pocCalculator.Calculate(sps, slice, nalRefIdc);
        IReadOnlyList<H264DecodedPictureOutput> priorOutputs = [];
        if (slice.IdrPicId.HasValue)
        {
            bool noOutput = slice.DecodedReferenceMarking?.NoOutputOfPriorPicsFlag ?? false;
            priorOutputs = _outputQueue.HandleIdr(noOutput);
        }
        else if (slice.DecodedReferenceMarking?.Operations.Any(
            operation => operation.MemoryManagementControlOperation == 5) == true)
        {
            // MMCO 5 invokes DPB bumping until every prior output picture has
            // been emitted before the new POC epoch begins.
            priorOutputs = _outputQueue.Flush();
        }

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
        if (sps.VuiParametersPresentFlag && vui is null)
            throw new FormatException("The SPS signals VUI parameters but no parsed VUI payload is available.");
        if (vui is { BitstreamRestrictionFlag: true })
        {
            if (vui.MaxNumReorderFrames is not uint reorderFrames ||
                vui.MaxDecFrameBuffering is not uint buffering)
                throw new FormatException("VUI bitstream restrictions must include max_num_reorder_frames and max_dec_frame_buffering.");
            if (reorderFrames > 16 || buffering > 16)
                throw new FormatException("VUI picture buffering values exceed the supported DPB limit.");
            if (reorderFrames > buffering)
                throw new FormatException("VUI max_num_reorder_frames cannot exceed max_dec_frame_buffering.");
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
