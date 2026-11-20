using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Derives the H.264 boundary strength for two adjacent 4x4 luma blocks in
/// frame-coded pictures.
/// </summary>
public static class H264DeblockingBoundaryStrengthDeriver
{
    public static int Derive(
        H264DeblockingBlockMetadata p,
        H264DeblockingBlockMetadata q,
        bool macroblockBoundary)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(q);

        if (p.IsIntra || q.IsIntra)
            return macroblockBoundary ? 4 : 3;
        if (p.HasNonZeroCoefficients || q.HasNonZeroCoefficients)
            return 2;

        bool sameDirectOrder =
            MatchesList(p.ReferenceIndexL0, p.MotionVectorL0,
                q.ReferenceIndexL0, q.MotionVectorL0) &&
            MatchesList(p.ReferenceIndexL1, p.MotionVectorL1,
                q.ReferenceIndexL1, q.MotionVectorL1);
        if (sameDirectOrder)
            return 0;

        bool swappedBiPredictionOrder =
            MatchesList(p.ReferenceIndexL0, p.MotionVectorL0,
                q.ReferenceIndexL1, q.MotionVectorL1) &&
            MatchesList(p.ReferenceIndexL1, p.MotionVectorL1,
                q.ReferenceIndexL0, q.MotionVectorL0);
        return swappedBiPredictionOrder ? 0 : 1;
    }

    private static bool MatchesList(
        int? pReferenceIndex,
        H264MotionVector? pMotion,
        int? qReferenceIndex,
        H264MotionVector? qMotion)
    {
        if (pReferenceIndex != qReferenceIndex)
            return false;
        if (pReferenceIndex is null)
            return pMotion is null && qMotion is null;

        if (pMotion is not H264MotionVector pVector ||
            qMotion is not H264MotionVector qVector)
            return false;

        return Math.Abs((long)pVector.Horizontal - qVector.Horizontal) < 4 &&
            Math.Abs((long)pVector.Vertical - qVector.Vertical) < 4;
    }
}
