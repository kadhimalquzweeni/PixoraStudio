namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// A decoded CAVLC residual block. Coefficients are stored in scan order,
/// beginning with the lowest-frequency scan position.
/// </summary>
public sealed record H264CavlcResidualBlock(
    int MaximumCoefficients,
    int TotalCoefficients,
    int TrailingOnes,
    int TotalZeros,
    IReadOnlyList<int> Coefficients);
