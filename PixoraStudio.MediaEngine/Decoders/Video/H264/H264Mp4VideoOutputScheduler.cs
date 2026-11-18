using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Joins the H.264 POC output scheduler to MP4 decode/presentation timestamps.
/// Samples remain associated with their surfaces while B-frame reordering
/// changes the order in which decoded pictures are released.
/// </summary>
public sealed class H264Mp4VideoOutputScheduler
{
    private readonly H264DecodedPictureOutputScheduler _scheduler;
    private readonly Dictionary<H264DecodedPictureSurface, H264Mp4VideoSample> _pendingSamples = [];

    public H264Mp4VideoOutputScheduler()
        : this(new H264DecodedPictureOutputScheduler())
    {
    }

    public H264Mp4VideoOutputScheduler(H264DecodedPictureOutputScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    public int PendingCount => _scheduler.PendingCount;
    public int PendingSampleCount => _pendingSamples.Count;

    public H264PictureOrderCount CalculatePictureOrderCount(
        H264SequenceParameterSet sps,
        H264SliceHeader slice,
        byte nalRefIdc) =>
        _scheduler.CalculatePictureOrderCount(sps, slice, nalRefIdc);

    public IReadOnlyList<H264Mp4DecodedVideoFrame> Enqueue(
        H264DecodedPictureSurface surface,
        H264Mp4VideoSample sourceSample,
        H264SequenceParameterSet sps,
        H264SliceHeader slice,
        byte nalRefIdc,
        H264PictureOrderCount pictureOrderCount,
        int? maxNumReorderFramesFallback = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(sourceSample);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(pictureOrderCount);
        if (sourceSample.Timescale == 0)
            throw new ArgumentOutOfRangeException(nameof(sourceSample),
                "MP4 sample timescale must be greater than zero.");
        if (!_pendingSamples.TryAdd(surface, sourceSample))
            throw new InvalidOperationException("The decoded surface already has a pending MP4 sample association.");

        IReadOnlyList<H264DecodedPictureOutput> outputs;
        try
        {
            outputs = _scheduler.Enqueue(
                surface, sps, slice, nalRefIdc, pictureOrderCount,
                maxNumReorderFramesFallback);
        }
        catch
        {
            _pendingSamples.Remove(surface);
            throw;
        }

        if (slice.IdrPicId.HasValue &&
            slice.DecodedReferenceMarking?.NoOutputOfPriorPicsFlag == true)
        {
            // The wrapped scheduler has discarded every prior pending output.
            // Remove only the old timestamp associations; retain this IDR one.
            H264DecodedPictureSurface[] discarded = _pendingSamples.Keys
                .Where(candidate => !ReferenceEquals(candidate, surface))
                .ToArray();
            foreach (H264DecodedPictureSurface candidate in discarded)
                _pendingSamples.Remove(candidate);
        }

        return Associate(outputs);
    }

    public IReadOnlyList<H264Mp4DecodedVideoFrame> Flush() =>
        Associate(_scheduler.Flush());

    public void Reset()
    {
        _scheduler.Reset();
        _pendingSamples.Clear();
    }

    private IReadOnlyList<H264Mp4DecodedVideoFrame> Associate(
        IReadOnlyList<H264DecodedPictureOutput> outputs)
    {
        List<H264Mp4DecodedVideoFrame> result = new(outputs.Count);
        foreach (H264DecodedPictureOutput output in outputs)
        {
            if (!_pendingSamples.Remove(output.Surface, out H264Mp4VideoSample? sample) || sample is null)
                throw new InvalidOperationException(
                    "The output scheduler released a picture without a matching MP4 sample timestamp association.");
            result.Add(new H264Mp4DecodedVideoFrame(
                output.Surface, output.PictureOrderCount, sample));
        }

        return result;
    }
}
