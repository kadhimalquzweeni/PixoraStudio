namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Inputs for filtering one horizontal or vertical H.264 block edge. QpP and
/// QpQ are the quantization parameters on the two sides of the edge.
/// </summary>
public sealed record H264DeblockingEdgeParameters(
    int BoundaryStrength,
    int QpP,
    int QpQ,
    int AlphaOffsetDiv2,
    int BetaOffsetDiv2,
    bool IsChroma);

/// <summary>Derived H.264 deblocking thresholds for one edge.</summary>
public readonly record struct H264DeblockingThresholds(
    int IndexA,
    int IndexB,
    int Alpha,
    int Beta,
    int Tc0);
