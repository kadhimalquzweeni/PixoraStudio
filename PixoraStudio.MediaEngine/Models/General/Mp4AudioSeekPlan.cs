namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Describes how to resume an AAC audio track at a presentation timestamp.
/// Decode from <see cref="FirstSampleToDecode"/> to rebuild AAC overlap state,
/// then discard <see cref="FramesToSkipInTargetSample"/> frames from the target
/// sample before emitting PCM.
/// </summary>
public sealed record Mp4AudioSeekPlan(
    uint FirstSampleToDecode,
    uint TargetSampleNumber,
    int FramesToSkipInTargetSample,
    double TargetPresentationTimeSeconds,
    double TargetSamplePresentationTimeSeconds,
    bool IsAtEndOfTrack);
