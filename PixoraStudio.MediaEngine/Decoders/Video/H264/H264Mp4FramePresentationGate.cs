namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Tracks the requested sample during a paused frame seek. It suppresses
/// earlier decoded pictures, forces the selected picture to present, and
/// prevents another picture with the same presentation timestamp from
/// replacing it until the playback clock advances.
/// </summary>
public sealed class H264Mp4FramePresentationGate
{
    private readonly uint? _requestedSampleNumber;
    private bool _hasReachedRequestedFrame;
    private double? _requestedPresentationTimeSeconds;

    public H264Mp4FramePresentationGate(uint? requestedSampleNumber = null)
    {
        if (requestedSampleNumber == 0)
            throw new ArgumentOutOfRangeException(nameof(requestedSampleNumber));

        _requestedSampleNumber = requestedSampleNumber;
        _hasReachedRequestedFrame = !requestedSampleNumber.HasValue;
    }

    public bool HasReachedRequestedFrame => _hasReachedRequestedFrame;

    /// <summary>
    /// Returns false for decoded pictures preceding the requested sample.
    /// The requested sample is returned with <paramref name="isRequestedFrame"/>
    /// set so the caller can present it regardless of the playback clock.
    /// </summary>
    public bool TryAcceptFrame(
        H264Mp4DecodedVideoFrame frame,
        out bool isRequestedFrame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        isRequestedFrame = false;

        if (!_requestedSampleNumber.HasValue)
            return true;

        if (!_hasReachedRequestedFrame)
        {
            if (frame.SourceSample.SampleNumber != _requestedSampleNumber.Value)
                return false;

            _hasReachedRequestedFrame = true;
            _requestedPresentationTimeSeconds =
                frame.SourceSample.PresentationTimeSeconds;
            isRequestedFrame = true;
            return true;
        }

        return true;
    }

    /// <summary>
    /// Holds another frame at (or within the timestamp tolerance of) the
    /// selected frame's PTS while the playback clock remains paused there.
    /// </summary>
    public bool ShouldWaitForFrame(
        H264Mp4DecodedVideoFrame frame,
        double playbackTimeSeconds,
        double timestampToleranceSeconds = 0.005)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!double.IsFinite(playbackTimeSeconds) || playbackTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(playbackTimeSeconds));
        if (!double.IsFinite(timestampToleranceSeconds) ||
            timestampToleranceSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(timestampToleranceSeconds));

        if (!_requestedSampleNumber.HasValue ||
            !_hasReachedRequestedFrame ||
            frame.SourceSample.SampleNumber == _requestedSampleNumber.Value)
            return false;

        double requestedTime = _requestedPresentationTimeSeconds!.Value;
        return frame.SourceSample.PresentationTimeSeconds <=
                requestedTime + timestampToleranceSeconds &&
            playbackTimeSeconds <=
                Math.Max(0, requestedTime) + timestampToleranceSeconds;
    }
}
