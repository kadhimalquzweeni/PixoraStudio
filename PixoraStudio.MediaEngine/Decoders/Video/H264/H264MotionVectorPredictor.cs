using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Implements the spatial list-0 motion-vector predictor selection rules for
/// frame-coded P-slice partitions. Candidate positions are A (left), B (above),
/// C (above-right), and D (above-left fallback when C is unavailable).
/// </summary>
public sealed class H264MotionVectorPredictor
{
    public H264MotionVector Predict(
        H264MotionVectorCandidate? left,
        H264MotionVectorCandidate? above,
        H264MotionVectorCandidate? aboveRight,
        H264MotionVectorCandidate? aboveLeft,
        int currentReferenceIndex,
        H264MotionVectorPartitionShape partitionShape = H264MotionVectorPartitionShape.SubMacroblock,
        int partitionIndex = 0)
    {
        if (currentReferenceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(currentReferenceIndex));

        ValidatePartition(partitionShape, partitionIndex);

        H264MotionVectorCandidate? c = aboveRight ?? aboveLeft;

        if (partitionShape == H264MotionVectorPartitionShape.SixteenByEight)
        {
            H264MotionVectorCandidate? preferred = partitionIndex == 0 ? above : left;
            if (preferred is { } candidate && candidate.ReferenceIndex == currentReferenceIndex)
                return candidate.MotionVector;
        }
        else if (partitionShape == H264MotionVectorPartitionShape.EightBySixteen)
        {
            H264MotionVectorCandidate? preferred = partitionIndex == 0 ? left : c;
            if (preferred is { } candidate && candidate.ReferenceIndex == currentReferenceIndex)
                return candidate.MotionVector;
        }

        H264MotionVectorCandidate?[] candidates = [left, above, c];
        H264MotionVectorCandidate? matching = null;
        int matchingCount = 0;

        foreach (H264MotionVectorCandidate? candidate in candidates)
        {
            if (candidate is { } available && available.ReferenceIndex == currentReferenceIndex)
            {
                matching = available;
                matchingCount++;
            }
        }

        if (matchingCount == 1)
            return matching!.Value.MotionVector;

        if (left is null && above is null && c is null)
            return new H264MotionVector(0, 0);

        H264MotionVector a = left?.MotionVector ?? new H264MotionVector(0, 0);
        H264MotionVector b = above?.MotionVector ?? new H264MotionVector(0, 0);
        H264MotionVector cVector = c?.MotionVector ?? new H264MotionVector(0, 0);

        return new H264MotionVector(
            Median(a.Horizontal, b.Horizontal, cVector.Horizontal),
            Median(a.Vertical, b.Vertical, cVector.Vertical));
    }

    private static int Median(int a, int b, int c) =>
        Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

    private static void ValidatePartition(H264MotionVectorPartitionShape shape, int index)
    {
        int maximumIndex = shape switch
        {
            H264MotionVectorPartitionShape.SixteenBySixteen => 0,
            H264MotionVectorPartitionShape.SixteenByEight or
                H264MotionVectorPartitionShape.EightBySixteen => 1,
            H264MotionVectorPartitionShape.SubMacroblock => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        if ((uint)index > (uint)maximumIndex)
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
