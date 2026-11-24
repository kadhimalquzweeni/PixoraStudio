using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Resolves list-0 motion vectors for parsed frame-coded P-slice inter
/// macroblocks. Partitions are processed in syntax order and recorded
/// immediately so subsequent partitions can use their reconstructed vectors.
/// This component does not perform reference sampling or pixel compensation.
/// </summary>
public sealed class H264InterMacroblockMotionVectorReconstructor
{
    private readonly H264MotionVectorPredictor _predictor;

    public H264InterMacroblockMotionVectorReconstructor()
        : this(new H264MotionVectorPredictor())
    {
    }

    public H264InterMacroblockMotionVectorReconstructor(
        H264MotionVectorPredictor predictor)
    {
        ArgumentNullException.ThrowIfNull(predictor);
        _predictor = predictor;
    }

    /// <summary>
    /// Reconstructs all list-0 vectors in one parsed P inter macroblock.
    /// macroblockX/Y are picture-relative macroblock coordinates. The active
    /// reference count is the number of entries in RefPicList0, after list
    /// modification has been applied.
    /// </summary>
    public IReadOnlyList<H264ReconstructedInterPartition> Reconstruct(
        H264InterMacroblockSyntax syntax,
        H264MotionVectorPictureState pictureState,
        int macroblockX,
        int macroblockY,
        int sliceId,
        int activeReferenceCount)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(pictureState);
        ValidateMacroblock(pictureState, macroblockX, macroblockY, sliceId);
        if (activeReferenceCount is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(activeReferenceCount));
        if (syntax.MacroblockType > 4)
            throw new NotSupportedException("Only the five list-0 P inter macroblock types are supported.");

        H264MotionVectorPartitionShape shape = syntax.MacroblockType switch
        {
            0 => H264MotionVectorPartitionShape.SixteenBySixteen,
            1 => H264MotionVectorPartitionShape.SixteenByEight,
            2 => H264MotionVectorPartitionShape.EightBySixteen,
            3 or 4 => H264MotionVectorPartitionShape.SubMacroblock,
            _ => throw new NotSupportedException()
        };

        if (syntax.Partitions.Count != (syntax.UsesSubMacroblockPartitions ? 4 :
            syntax.MacroblockType == 0 ? 1 : 2))
            throw new FormatException("The inter macroblock partition count does not match mb_type.");

        if (syntax.UsesSubMacroblockPartitions != (syntax.MacroblockType is 3 or 4))
            throw new FormatException("The sub-macroblock partition flag does not match mb_type.");
        if (syntax.ReferenceIndexForcedToZero != (syntax.MacroblockType == 4))
            throw new FormatException("The forced-reference-index flag does not match P_8x8ref0.");

        ValidateSyntax(syntax, activeReferenceCount);

        List<H264ReconstructedInterPartition> result = [];
        int macroblockBlockX = checked(macroblockX * 4);
        int macroblockBlockY = checked(macroblockY * 4);

        for (int partitionIndex = 0; partitionIndex < syntax.Partitions.Count; partitionIndex++)
        {
            H264InterPredictionPartition partition = syntax.Partitions[partitionIndex];
            (int expectedX, int expectedY, int expectedWidth, int expectedHeight) =
                GetExpectedPartition(syntax.MacroblockType, partitionIndex);
            if (partition.X != expectedX || partition.Y != expectedY ||
                partition.Width != expectedWidth || partition.Height != expectedHeight)
                throw new FormatException("An inter partition does not match its macroblock partition shape.");
            if (partition.ReferenceIndex >= activeReferenceCount)
                throw new FormatException("The inter partition references an entry outside RefPicList0.");
            if (partition.SubPartitions.Count == 0)
                throw new FormatException("An inter partition must contain at least one sub-partition.");

            for (int subIndex = 0; subIndex < partition.SubPartitions.Count; subIndex++)
            {
                H264InterSubPartition sub = partition.SubPartitions[subIndex];
                if (sub.MotionVectorDifferences.Count != 1)
                    throw new FormatException("Each list-0 sub-partition must contain exactly one MVD pair.");
                if ((sub.X | sub.Y | sub.Width | sub.Height) % 4 != 0 ||
                    sub.X < partition.X || sub.Y < partition.Y ||
                    sub.Width <= 0 || sub.Height <= 0 ||
                    sub.X + sub.Width > partition.X + partition.Width ||
                    sub.Y + sub.Height > partition.Y + partition.Height)
                    throw new FormatException("An inter sub-partition has invalid partition-relative geometry.");

                int x = checked(macroblockBlockX + sub.X / 4);
                int y = checked(macroblockBlockY + sub.Y / 4);
                int width = sub.Width / 4;
                int height = sub.Height / 4;
                int predictorPartitionIndex = shape == H264MotionVectorPartitionShape.SubMacroblock
                    ? subIndex
                    : partitionIndex;

                H264MotionVector motionVector = pictureState.ReconstructAndRecord(
                    _predictor, x, y, width, height, sliceId,
                    checked((int)partition.ReferenceIndex),
                    sub.MotionVectorDifferences[0], shape, predictorPartitionIndex);

                result.Add(new H264ReconstructedInterPartition(
                    partitionIndex, subIndex, x, y, width, height,
                    checked((int)partition.ReferenceIndex), motionVector));
            }
        }

        return result;
    }

    /// <summary>
    /// Derives and records a P_Skip macroblock's 16x16 list-0 motion vector.
    /// H.264's special P_Skip rule forces zero when A or B is unavailable,
    /// or when either available A/B candidate has ref_idx 0 and a zero vector.
    /// </summary>
    public H264ReconstructedInterPartition ReconstructSkipped(
        H264MotionVectorPictureState pictureState,
        int macroblockX,
        int macroblockY,
        int sliceId)
    {
        ArgumentNullException.ThrowIfNull(pictureState);
        ValidateMacroblock(pictureState, macroblockX, macroblockY, sliceId);

        int x = checked(macroblockX * 4);
        int y = checked(macroblockY * 4);
        H264MotionVectorNeighbourCandidates candidates =
            pictureState.GetCandidates(x, y, 4, 4, sliceId);

        bool forceZero = candidates.Left is null || candidates.Above is null;
        if (!forceZero)
        {
            H264MotionVectorCandidate left = candidates.Left!.Value;
            H264MotionVectorCandidate above = candidates.Above!.Value;
            forceZero =
                (left.ReferenceIndex == 0 && left.MotionVector == new H264MotionVector(0, 0)) ||
                (above.ReferenceIndex == 0 && above.MotionVector == new H264MotionVector(0, 0));
        }

        H264MotionVector motionVector = forceZero
            ? new H264MotionVector(0, 0)
            : _predictor.Predict(
                candidates.Left, candidates.Above, candidates.AboveRight,
                candidates.AboveLeft, 0,
                H264MotionVectorPartitionShape.SixteenBySixteen);

        pictureState.RecordInterPartition(x, y, 4, 4, sliceId, 0, motionVector);
        return new H264ReconstructedInterPartition(
            0, 0, x, y, 4, 4, 0, motionVector);
    }

    private static void ValidateSyntax(
        H264InterMacroblockSyntax syntax,
        int activeReferenceCount)
    {
        for (int partitionIndex = 0; partitionIndex < syntax.Partitions.Count; partitionIndex++)
        {
            H264InterPredictionPartition partition = syntax.Partitions[partitionIndex];
            (int expectedX, int expectedY, int expectedWidth, int expectedHeight) =
                GetExpectedPartition(syntax.MacroblockType, partitionIndex);
            if (partition.X != expectedX || partition.Y != expectedY ||
                partition.Width != expectedWidth || partition.Height != expectedHeight)
                throw new FormatException("An inter partition does not match its macroblock partition shape.");
            if (partition.ReferenceIndex >= activeReferenceCount)
                throw new FormatException("The inter partition references an entry outside RefPicList0.");
            if (partition.SubPartitions.Count == 0)
                throw new FormatException("An inter partition must contain at least one sub-partition.");

            bool[,] covered = new bool[4, 4];
            foreach (H264InterSubPartition sub in partition.SubPartitions)
            {
                if (sub.MotionVectorDifferences.Count != 1)
                    throw new FormatException("Each list-0 sub-partition must contain exactly one MVD pair.");
                if ((sub.X | sub.Y | sub.Width | sub.Height) % 4 != 0 ||
                    sub.X < partition.X || sub.Y < partition.Y ||
                    sub.Width <= 0 || sub.Height <= 0 ||
                    sub.X + sub.Width > partition.X + partition.Width ||
                    sub.Y + sub.Height > partition.Y + partition.Height)
                    throw new FormatException("An inter sub-partition has invalid partition-relative geometry.");

                for (int y = sub.Y / 4; y < (sub.Y + sub.Height) / 4; y++)
                for (int x = sub.X / 4; x < (sub.X + sub.Width) / 4; x++)
                {
                    if (covered[x, y])
                        throw new FormatException("Inter sub-partitions overlap.");
                    covered[x, y] = true;
                }
            }

            for (int y = partition.Y / 4; y < (partition.Y + partition.Height) / 4; y++)
            for (int x = partition.X / 4; x < (partition.X + partition.Width) / 4; x++)
                if (!covered[x, y])
                    throw new FormatException("Inter sub-partitions do not cover their complete parent partition.");
        }
    }

    private static (int X, int Y, int Width, int Height) GetExpectedPartition(
        uint macroblockType,
        int partitionIndex) =>
        macroblockType switch
        {
            0 when partitionIndex == 0 => (0, 0, 16, 16),
            1 when partitionIndex == 0 => (0, 0, 16, 8),
            1 when partitionIndex == 1 => (0, 8, 16, 8),
            2 when partitionIndex == 0 => (0, 0, 8, 16),
            2 when partitionIndex == 1 => (8, 0, 8, 16),
            3 or 4 when partitionIndex == 0 => (0, 0, 8, 8),
            3 or 4 when partitionIndex == 1 => (8, 0, 8, 8),
            3 or 4 when partitionIndex == 2 => (0, 8, 8, 8),
            3 or 4 when partitionIndex == 3 => (8, 8, 8, 8),
            _ => throw new FormatException("The inter macroblock partition index is invalid.")
        };

    private static void ValidateMacroblock(
        H264MotionVectorPictureState state,
        int macroblockX,
        int macroblockY,
        int sliceId)
    {
        if ((uint)macroblockX >= (uint)state.MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if ((uint)macroblockY >= (uint)state.MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
    }
}
