using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Decodes an MP4 AVC track in decode order and yields pictures in H.264
/// presentation order. The caller supplies the parsed MP4 movie/track boxes;
/// all emitted frames retain their originating sample timestamps.
/// </summary>
public sealed class H264Mp4TrackDecoder
{
    private readonly Mp4TrackSamplePipelineReader _samplePipelineReader = new();
    private readonly H264Mp4TrackSampleReader _sampleReader = new();

    /// <summary>
    /// Reads and decodes every sample from the selected MP4 video track.
    /// </summary>
    public IEnumerable<H264Mp4DecodedVideoFrame> Decode(
        Stream stream,
        Mp4Box root,
        Mp4Box trackBox,
        Mp4Track track,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(trackBox);
        ArgumentNullException.ThrowIfNull(track);

        IEnumerable<Mp4EncodedSample> samples = _samplePipelineReader.Read(
            stream, root, trackBox, track, cancellationToken);
        return DecodeSamples(
            track, samples, maxNumReorderFramesFallback, cancellationToken);
    }

    /// <summary>
    /// Decodes a video track from an already parsed MP4 media-info object.
    /// The track's sample enumerator supplies encoded samples in decode order.
    /// </summary>
    public IEnumerable<H264Mp4DecodedVideoFrame> Decode(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mediaInfo);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("The MP4 stream must support reading and seeking.", nameof(stream));

        Mp4MediaTrack mediaTrack = mediaInfo.GetTrack(trackId) ??
            throw new ArgumentException($"MP4 track {trackId} does not exist.", nameof(trackId));
        if (mediaTrack.HandlerType != Mp4HandlerType.Video)
            throw new NotSupportedException($"MP4 track {trackId} is not a video track.");

        IEnumerable<Mp4EncodedSample> samples = mediaTrack.ReadSamples(
            stream, mediaInfo.MediaDataRegions, cancellationToken);
        return DecodeSamples(
            mediaTrack.Track, samples, maxNumReorderFramesFallback, cancellationToken);
    }

    /// <summary>
    /// Seeks to a presentation time in an MP4 AVC video track. Decoding starts
    /// at the preceding sync sample and continues in decode order so reference
    /// pictures and POC state are rebuilt correctly. Frames before the target
    /// presentation interval are discarded; the first overlapping frame and
    /// all subsequent presentation frames are returned.
    /// </summary>
    public IEnumerable<H264Mp4DecodedVideoFrame> DecodeFromTime(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        double targetPresentationTimeSeconds,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mediaInfo);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("The MP4 stream must support reading and seeking.", nameof(stream));

        Mp4MediaTrack mediaTrack = mediaInfo.GetTrack(trackId) ??
            throw new ArgumentException($"MP4 track {trackId} does not exist.", nameof(trackId));
        if (mediaTrack.HandlerType != Mp4HandlerType.Video)
            throw new NotSupportedException($"MP4 track {trackId} is not a video track.");

        H264Mp4VideoSeekPlan plan = new H264Mp4VideoSeekPlanner().Plan(
            mediaTrack, targetPresentationTimeSeconds);
        uint remainingSamples = checked(mediaTrack.SampleCount - plan.FirstSampleToDecode + 1);
        IEnumerable<Mp4EncodedSample> samples = mediaTrack.ReadSamples(
            stream,
            plan.FirstSampleToDecode,
            remainingSamples,
            mediaInfo.MediaDataRegions,
            cancellationToken);

        return DecodeFromSeekPoint(
            mediaTrack.Track,
            samples,
            plan,
            maxNumReorderFramesFallback,
            cancellationToken);
    }

    private IEnumerable<H264Mp4DecodedVideoFrame> DecodeFromSeekPoint(
        Mp4Track track,
        IEnumerable<Mp4EncodedSample> samples,
        H264Mp4VideoSeekPlan plan,
        int? maxNumReorderFramesFallback,
        CancellationToken cancellationToken)
    {
        foreach (H264Mp4DecodedVideoFrame frame in DecodeSamples(
            track, samples, maxNumReorderFramesFallback, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            long presentationEnd;
            try
            {
                presentationEnd = checked(frame.PresentationTimeTicks + frame.DurationTicks);
            }
            catch (OverflowException exception)
            {
                throw new FormatException(
                    "A decoded MP4 frame has an overflowing presentation interval.", exception);
            }

            if (presentationEnd > plan.TargetPresentationTimeTicks)
                yield return frame;
        }
    }

    /// <summary>
    /// Decodes an already-enumerated sequence of MP4 samples. This overload is
    /// useful for callers that manage sample storage or for deterministic tests.
    /// Samples must be supplied contiguously in decode order.
    /// </summary>
    public IEnumerable<H264Mp4DecodedVideoFrame> DecodeSamples(
        Mp4Track track,
        IEnumerable<Mp4EncodedSample> samples,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(samples);
        if (track.HandlerType != Mp4HandlerType.Video)
            throw new NotSupportedException("The selected MP4 track is not a video track.");

        return DecodeIterator(
            track, samples, maxNumReorderFramesFallback, cancellationToken);
    }

    private IEnumerable<H264Mp4DecodedVideoFrame> DecodeIterator(
        Mp4Track track,
        IEnumerable<Mp4EncodedSample> samples,
        int? maxNumReorderFramesFallback,
        CancellationToken cancellationToken)
    {
        H264Mp4AccessUnitDecoder? decoder = null;
        foreach (Mp4EncodedSample encodedSample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            H264Mp4VideoSample sample = _sampleReader.Read(track, encodedSample);
            decoder ??= H264Mp4AccessUnitDecoder.CreateFromSample(
                sample, maxNumReorderFramesFallback);

            H264Mp4AccessUnitDecodeResult? result = decoder.Decode(sample);
            if (result is null)
                continue;

            foreach (H264Mp4DecodedVideoFrame frame in result.ReadyFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return frame;
            }
        }

        if (decoder is null)
            yield break;

        foreach (H264Mp4DecodedVideoFrame frame in decoder.Flush())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
        }
    }
}
