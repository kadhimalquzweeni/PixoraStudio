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
            MatchesList(p.ReferencePictureL0, p.MotionVectorL0,
                q.ReferencePictureL0, q.MotionVectorL0) &&
            MatchesList(p.ReferencePictureL1, p.MotionVectorL1,
                q.ReferencePictureL1, q.MotionVectorL1);
        if (sameDirectOrder)
            return 0;

        bool swappedBiPredictionOrder =
            MatchesList(p.ReferencePictureL0, p.MotionVectorL0,
                q.ReferencePictureL1, q.MotionVectorL1) &&
            MatchesList(p.ReferencePictureL1, p.MotionVectorL1,
                q.ReferencePictureL0, q.MotionVectorL0);
        return swappedBiPredictionOrder ? 0 : 1;
    }

    private static bool MatchesList(
        H264ReferencePicture? pReference,
        H264MotionVector? pMotion,
        H264ReferencePicture? qReference,
        H264MotionVector? qMotion)
    {
        if (pReference is null || qReference is null)
            return pReference is null && qReference is null &&
                pMotion is null && qMotion is null;

        // Reference identity is not determined by POC alone: distinct
        // short-/long-term pictures can share a POC while remaining different
        // entries in the decoded reference-picture set.
        if (pReference != qReference ||
            pMotion is not H264MotionVector pVector ||
            qMotion is not H264MotionVector qVector)
            return false;

        return Math.Abs((long)pVector.Horizontal - qVector.Horizontal) < 4 &&
            Math.Abs((long)pVector.Vertical - qVector.Vertical) < 4;
    }
}
