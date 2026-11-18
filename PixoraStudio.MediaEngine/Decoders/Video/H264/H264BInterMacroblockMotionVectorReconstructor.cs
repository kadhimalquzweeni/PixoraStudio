using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reconstructs explicit list-0/list-1 motion vectors for frame-coded B-slice
/// inter partitions. Spatial direct prediction and provider-based temporal
/// direct prediction are integrated.
/// </summary>
public sealed class H264BInterMacroblockMotionVectorReconstructor
{
    private readonly H264MotionVectorPredictor _predictor;
    private readonly H264BSpatialDirectMotionVectorDeriver _directDeriver;
    private readonly H264BTemporalDirectMotionVectorDeriver _temporalDeriver = new();

    public H264BInterMacroblockMotionVectorReconstructor()
        : this(new H264MotionVectorPredictor(), new H264BSpatialDirectMotionVectorDeriver())
    {
    }

    public H264BInterMacroblockMotionVectorReconstructor(H264MotionVectorPredictor predictor)
        : this(predictor, new H264BSpatialDirectMotionVectorDeriver())
    {
    }

    public H264BInterMacroblockMotionVectorReconstructor(
        H264MotionVectorPredictor predictor,
        H264BSpatialDirectMotionVectorDeriver directDeriver)
    {
        ArgumentNullException.ThrowIfNull(predictor);
        ArgumentNullException.ThrowIfNull(directDeriver);
        _predictor = predictor;
        _directDeriver = directDeriver;
    }

    public IReadOnlyList<H264ReconstructedBInterSubPartition> Reconstruct(
        H264BMacroblockSyntax syntax,
        H264MotionVectorPictureState motionVectorStateL0,
        H264MotionVectorPictureState motionVectorStateL1,
        int macroblockX,
        int macroblockY,
        int sliceId,
        int activeReferenceCountL0,
        int activeReferenceCountL1,
        bool directSpatialPrediction = false,
        bool direct8x8InferenceFlag = true,
        Func<int, int, int, int, H264TemporalDirectPredictionContext?>? temporalContextProvider = null)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(motionVectorStateL0);
        ArgumentNullException.ThrowIfNull(motionVectorStateL1);
        ValidateContext(motionVectorStateL0, macroblockX, macroblockY, sliceId,
            nameof(motionVectorStateL0));
        ValidateContext(motionVectorStateL1, macroblockX, macroblockY, sliceId,
            nameof(motionVectorStateL1));
        if (motionVectorStateL0.MacroblockWidth != motionVectorStateL1.MacroblockWidth ||
            motionVectorStateL0.MacroblockHeight != motionVectorStateL1.MacroblockHeight)
            throw new ArgumentException("List-0 and list-1 motion states must describe the same picture geometry.");
        if (activeReferenceCountL0 is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(activeReferenceCountL0));
        if (activeReferenceCountL1 is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(activeReferenceCountL1));
        if (syntax.MacroblockType > 22)
            throw new NotSupportedException("Only frame-coded B inter macroblock types 0..22 are supported.");

        H264MotionVectorPartitionShape shape = GetPartitionShape(syntax);
        int macroblockBlockX = checked(macroblockX * 4);
        int macroblockBlockY = checked(macroblockY * 4);
        List<H264ReconstructedBInterSubPartition> result = [];

        for (int partitionIndex = 0; partitionIndex < syntax.Partitions.Count; partitionIndex++)
        {
            H264BPredictionPartition partition = syntax.Partitions[partitionIndex];
            ValidatePartition(partition);
            if ((partition.Direction & H264BPredictionDirection.List0) != 0)
            {
                if (partition.ReferenceIndexL0 is not uint reference || reference >= activeReferenceCountL0)
                    throw new FormatException("The B partition has an invalid list-0 reference index.");
            }
            else if (partition.ReferenceIndexL0 is not null)
                throw new FormatException("A partition not using list 0 must not carry a list-0 reference index.");

            if ((partition.Direction & H264BPredictionDirection.List1) != 0)
            {
                if (partition.ReferenceIndexL1 is not uint reference || reference >= activeReferenceCountL1)
                    throw new FormatException("The B partition has an invalid list-1 reference index.");
            }
            else if (partition.ReferenceIndexL1 is not null)
                throw new FormatException("A partition not using list 1 must not carry a list-1 reference index.");

            for (int subIndex = 0; subIndex < partition.SubPartitions.Count; subIndex++)
            {
                H264BSubPartition sub = partition.SubPartitions[subIndex];
                if (sub.Direction == H264BPredictionDirection.Direct)
                {
                    if (partition.Direction != H264BPredictionDirection.Direct ||
                        partition.ReferenceIndexL0 is not null ||
                        partition.ReferenceIndexL1 is not null)
                        throw new FormatException("Direct B sub-partitions must belong to a direct parent partition and carry no explicit reference indices.");
                    if (directSpatialPrediction)
                    {
                        result.AddRange(_directDeriver.Derive(
                            motionVectorStateL0, motionVectorStateL1,
                            macroblockX, macroblockY,
                            sub.X / 4, sub.Y / 4, sub.Width / 4, sub.Height / 4,
                            partitionIndex, sliceId, direct8x8InferenceFlag));
                    }
                    else
                    {
                        if (temporalContextProvider is null)
                            throw new NotSupportedException("Temporal direct prediction requires a collocated-picture metadata provider.");
                        int originX = checked(macroblockBlockX + sub.X / 4);
                        int originY = checked(macroblockBlockY + sub.Y / 4);
                        int regionWidth = sub.Width / 4;
                        int regionHeight = sub.Height / 4;
                        int step = direct8x8InferenceFlag ? 2 : 1;
                        int temporalSubIndex = 0;
                        for (int offsetY = 0; offsetY < regionHeight; offsetY += step)
                        for (int offsetX = 0; offsetX < regionWidth; offsetX += step)
                        {
                            int x = originX + offsetX;
                            int y = originY + offsetY;
                            H264TemporalDirectPredictionContext? context =
                                temporalContextProvider(x, y, step, step);
                            H264TemporalDirectMotionVectorResult vectors = context is null
                                ? new H264TemporalDirectMotionVectorResult(
                                    new H264MotionVector(0, 0), new H264MotionVector(0, 0), 256)
                                : _temporalDeriver.Derive(context);
                            motionVectorStateL0.RecordInterPartition(
                                x, y, step, step, sliceId, 0, vectors.MotionVectorL0);
                            motionVectorStateL1.RecordInterPartition(
                                x, y, step, step, sliceId, 0, vectors.MotionVectorL1);
                            result.Add(new H264ReconstructedBInterSubPartition(
                                partitionIndex, temporalSubIndex++, x, y, step, step,
                                0, 0, vectors.MotionVectorL0, vectors.MotionVectorL1));
                        }
                    }
                    continue;
                }
                if (partition.Direction == H264BPredictionDirection.Direct ||
                    sub.Direction != partition.Direction)
                    throw new FormatException("A non-direct B sub-partition direction must match its non-direct parent partition.");
                int x = checked(macroblockBlockX + sub.X / 4);
                int y = checked(macroblockBlockY + sub.Y / 4);
                int width = sub.Width / 4;
                int height = sub.Height / 4;
                H264MotionVector? mv0 = null;
                H264MotionVector? mv1 = null;

                if ((sub.Direction & H264BPredictionDirection.List0) != 0)
                {
                    if (sub.MotionVectorDifferenceL0 is not H264MotionVectorDifference mvd0 ||
                        partition.ReferenceIndexL0 is not uint reference0)
                        throw new FormatException("A list-0 B sub-partition is missing its reference index or MVD.");
                    mv0 = motionVectorStateL0.ReconstructAndRecord(
                        _predictor, x, y, width, height, sliceId,
                        checked((int)reference0), mvd0, shape, partitionIndex);
                }
                else if (sub.MotionVectorDifferenceL0 is not null)
                    throw new FormatException("A sub-partition not using list 0 must not carry list-0 MVD syntax.");

                if ((sub.Direction & H264BPredictionDirection.List1) != 0)
                {
                    if (sub.MotionVectorDifferenceL1 is not H264MotionVectorDifference mvd1 ||
                        partition.ReferenceIndexL1 is not uint reference1)
                        throw new FormatException("A list-1 B sub-partition is missing its reference index or MVD.");
                    mv1 = motionVectorStateL1.ReconstructAndRecord(
                        _predictor, x, y, width, height, sliceId,
                        checked((int)reference1), mvd1, shape, partitionIndex);
                }
                else if (sub.MotionVectorDifferenceL1 is not null)
                    throw new FormatException("A sub-partition not using list 1 must not carry list-1 MVD syntax.");

                result.Add(new H264ReconstructedBInterSubPartition(
                    partitionIndex, subIndex, x, y, width, height,
                    partition.ReferenceIndexL0 is uint r0 ? checked((int)r0) : null,
                    partition.ReferenceIndexL1 is uint r1 ? checked((int)r1) : null,
                    mv0, mv1));
            }
        }

        return result;
    }

    private static H264MotionVectorPartitionShape GetPartitionShape(H264BMacroblockSyntax syntax)
    {
        if (syntax.UsesSubMacroblockPartitions)
            return H264MotionVectorPartitionShape.SubMacroblock;
        if (syntax.MacroblockType is >= 4 and <= 21)
            return (syntax.MacroblockType & 1) == 0
                ? H264MotionVectorPartitionShape.SixteenByEight
                : H264MotionVectorPartitionShape.EightBySixteen;
        return H264MotionVectorPartitionShape.SixteenBySixteen;
    }

    private static void ValidatePartition(H264BPredictionPartition partition)
    {
        if (partition.Width <= 0 || partition.Height <= 0 ||
            partition.X < 0 || partition.Y < 0 ||
            partition.X > 16 || partition.Y > 16 ||
            partition.Width > 16 - partition.X ||
            partition.Height > 16 - partition.Y ||
            partition.SubPartitions.Count == 0)
            throw new FormatException("A B macroblock partition has invalid geometry or no sub-partitions.");

        bool[,] covered = new bool[16, 16];
        foreach (H264BSubPartition sub in partition.SubPartitions)
        {
            if (sub.Width <= 0 || sub.Height <= 0 ||
                sub.X < partition.X || sub.Y < partition.Y ||
                sub.X > partition.X + partition.Width ||
                sub.Y > partition.Y + partition.Height ||
                sub.Width > partition.X + partition.Width - sub.X ||
                sub.Height > partition.Y + partition.Height - sub.Y ||
                (sub.X | sub.Y | sub.Width | sub.Height) % 4 != 0)
                throw new FormatException("A B sub-partition has invalid 4x4-aligned geometry.");
            for (int y = sub.Y; y < sub.Y + sub.Height; y++)
            for (int x = sub.X; x < sub.X + sub.Width; x++)
            {
                if (covered[x, y])
                    throw new FormatException("B sub-partitions overlap.");
                covered[x, y] = true;
            }
        }

        for (int y = partition.Y; y < partition.Y + partition.Height; y++)
        for (int x = partition.X; x < partition.X + partition.Width; x++)
            if (!covered[x, y])
                throw new FormatException("B sub-partitions do not cover their parent partition.");
    }

    private static void ValidateContext(
        H264MotionVectorPictureState state, int x, int y, int sliceId, string parameterName)
    {
        if ((uint)x >= (uint)state.MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)state.MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
    }
}
