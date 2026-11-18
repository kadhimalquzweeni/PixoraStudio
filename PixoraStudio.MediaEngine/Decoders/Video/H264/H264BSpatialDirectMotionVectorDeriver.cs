using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Derives frame-coded spatial direct motion for one B-slice direct region.
/// Temporal direct prediction is a separate operation because it requires the
/// collocated picture and its stored motion/reference metadata.
/// </summary>
public sealed class H264BSpatialDirectMotionVectorDeriver
{
    private readonly H264MotionVectorPredictor _predictor;

    public H264BSpatialDirectMotionVectorDeriver()
        : this(new H264MotionVectorPredictor())
    {
    }

    public H264BSpatialDirectMotionVectorDeriver(H264MotionVectorPredictor predictor)
    {
        ArgumentNullException.ThrowIfNull(predictor);
        _predictor = predictor;
    }

    public IReadOnlyList<H264ReconstructedBInterSubPartition> Derive(
        H264MotionVectorPictureState motionVectorStateL0,
        H264MotionVectorPictureState motionVectorStateL1,
        int macroblockX,
        int macroblockY,
        int partitionX,
        int partitionY,
        int partitionWidth,
        int partitionHeight,
        int macroblockPartitionIndex,
        int sliceId,
        bool direct8x8InferenceFlag,
        Func<int, int, int, int, bool>? colZeroFlagProvider = null)
    {
        ArgumentNullException.ThrowIfNull(motionVectorStateL0);
        ArgumentNullException.ThrowIfNull(motionVectorStateL1);
        if (motionVectorStateL0.MacroblockWidth != motionVectorStateL1.MacroblockWidth ||
            motionVectorStateL0.MacroblockHeight != motionVectorStateL1.MacroblockHeight)
            throw new ArgumentException("List-0 and list-1 motion states must describe the same picture geometry.");
        if ((uint)macroblockX >= (uint)motionVectorStateL0.MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if ((uint)macroblockY >= (uint)motionVectorStateL0.MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (partitionX < 0 || partitionY < 0 ||
            partitionWidth <= 0 || partitionHeight <= 0 ||
            partitionX > 4 || partitionY > 4 ||
            partitionWidth > 4 - partitionX || partitionHeight > 4 - partitionY)
            throw new ArgumentOutOfRangeException(nameof(partitionWidth));
        if ((partitionX | partitionY | partitionWidth | partitionHeight) % 2 != 0)
            throw new ArgumentException("A direct region must be aligned to 8x8 luma samples.");

        int blockX = checked(macroblockX * 4 + partitionX);
        int blockY = checked(macroblockY * 4 + partitionY);
        int step = direct8x8InferenceFlag ? 2 : 1;
        List<H264ReconstructedBInterSubPartition> result = [];
        int subIndex = 0;

        for (int y = 0; y < partitionHeight; y += step)
        for (int x = 0; x < partitionWidth; x += step)
        {
            int regionX = blockX + x;
            int regionY = blockY + y;
            H264MotionVectorNeighbourCandidates candidates0 =
                motionVectorStateL0.GetCandidates(regionX, regionY, step, step, sliceId);
            H264MotionVectorNeighbourCandidates candidates1 =
                motionVectorStateL1.GetCandidates(regionX, regionY, step, step, sliceId);

            int referenceIndex0 = MinimumReferenceIndex(candidates0);
            int referenceIndex1 = MinimumReferenceIndex(candidates1);
            bool directZeroPrediction = AllReferencesUnavailable(candidates0) &&
                AllReferencesUnavailable(candidates1);

            bool colZeroFlag = colZeroFlagProvider?.Invoke(regionX, regionY, step, step) ?? false;
            H264MotionVector vector0 = directZeroPrediction ||
                (referenceIndex0 == 0 && colZeroFlag)
                ? new H264MotionVector(0, 0)
                : _predictor.Predict(candidates0.Left, candidates0.Above,
                    candidates0.AboveRight, candidates0.AboveLeft, referenceIndex0);
            H264MotionVector vector1 = directZeroPrediction ||
                (referenceIndex1 == 0 && colZeroFlag)
                ? new H264MotionVector(0, 0)
                : _predictor.Predict(candidates1.Left, candidates1.Above,
                    candidates1.AboveRight, candidates1.AboveLeft, referenceIndex1);

            motionVectorStateL0.RecordInterPartition(
                regionX, regionY, step, step, sliceId, referenceIndex0, vector0);
            motionVectorStateL1.RecordInterPartition(
                regionX, regionY, step, step, sliceId, referenceIndex1, vector1);

            result.Add(new H264ReconstructedBInterSubPartition(
                macroblockPartitionIndex, subIndex++,
                regionX, regionY, step, step,
                referenceIndex0, referenceIndex1, vector0, vector1));
        }

        return result;
    }

    private static int MinimumReferenceIndex(H264MotionVectorNeighbourCandidates candidates)
    {
        int minimum = int.MaxValue;
        H264MotionVectorCandidate? c = candidates.AboveRight ?? candidates.AboveLeft;
        if (candidates.Left is { } left) minimum = Math.Min(minimum, left.ReferenceIndex);
        if (candidates.Above is { } above) minimum = Math.Min(minimum, above.ReferenceIndex);
        if (c is { } aboveCandidate) minimum = Math.Min(minimum, aboveCandidate.ReferenceIndex);
        return minimum == int.MaxValue ? 0 : minimum;
    }

    private static bool AllReferencesUnavailable(H264MotionVectorNeighbourCandidates candidates) =>
        candidates.Left is null &&
        candidates.Above is null &&
        candidates.AboveRight is null &&
        candidates.AboveLeft is null;

}
