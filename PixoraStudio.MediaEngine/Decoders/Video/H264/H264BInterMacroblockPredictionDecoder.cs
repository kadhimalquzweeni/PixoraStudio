using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Generates list-0/list-1 motion-compensated predictions for explicit
/// frame-coded B partitions and combines bi-predicted blocks using the
/// slice's weighted_bipred_idc mode.
/// </summary>
public sealed class H264BInterMacroblockPredictionDecoder
{
    private readonly H264MotionCompensator _motionCompensator;

    public H264BInterMacroblockPredictionDecoder()
        : this(new H264MotionCompensator())
    {
    }

    public H264BInterMacroblockPredictionDecoder(H264MotionCompensator motionCompensator)
    {
        ArgumentNullException.ThrowIfNull(motionCompensator);
        _motionCompensator = motionCompensator;
    }

    public IReadOnlyList<H264BInterMacroblockPrediction> DecodeAndPredict(
        IReadOnlyList<H264ReconstructedBInterSubPartition> partitions,
        IReadOnlyList<H264DecodedPictureSurface> referenceList0,
        IReadOnlyList<H264DecodedPictureSurface> referenceList1,
        H264BReferencePictureLists referencePictureLists,
        uint chromaFormatIdc,
        int currentPictureOrderCount,
        byte weightedBipredIdc,
        H264PredictionWeightTable? predictionWeights = null)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        ArgumentNullException.ThrowIfNull(referenceList0);
        ArgumentNullException.ThrowIfNull(referenceList1);
        ArgumentNullException.ThrowIfNull(referencePictureLists);
        if (chromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("B inter prediction supports frame-only 8-bit 4:2:0 and 4:2:2.");
        if (weightedBipredIdc > 2)
            throw new ArgumentOutOfRangeException(nameof(weightedBipredIdc));
        if (referenceList0.Count != referencePictureLists.List0.Count ||
            referenceList1.Count != referencePictureLists.List1.Count ||
            referenceList0.Count == 0 || referenceList1.Count == 0)
            throw new ArgumentException("Decoded reference surfaces must match the active B reference lists.");
        if (referenceList0.Any(item => item is null) || referenceList1.Any(item => item is null))
            throw new ArgumentException("B reference lists cannot contain null surfaces.");
        if (weightedBipredIdc == 1 && predictionWeights is null)
            throw new ArgumentNullException(nameof(predictionWeights),
                "Explicit weighted bi-prediction requires the parsed slice prediction-weight table.");

        List<H264BInterMacroblockPrediction> result = new(partitions.Count);
        foreach (H264ReconstructedBInterSubPartition partition in partitions)
        {
            ArgumentNullException.ThrowIfNull(partition);
            ValidatePartition(partition);
            H264MotionCompensatedBlock? prediction0 = null;
            H264MotionCompensatedBlock? prediction1 = null;

            if (partition.MotionVectorL0 is H264MotionVector vector0)
            {
                if (partition.ReferenceIndexL0 is not int referenceIndex0 ||
                    (uint)referenceIndex0 >= (uint)referenceList0.Count)
                    throw new FormatException("The reconstructed B partition has an invalid list-0 reference.");
                prediction0 = _motionCompensator.Predict(
                    referenceList0[referenceIndex0],
                    checked(partition.X * 4), checked(partition.Y * 4),
                    checked(partition.Width * 4), checked(partition.Height * 4),
                    vector0);
            }
            else if (partition.ReferenceIndexL0 is not null)
                throw new FormatException("A B partition without a list-0 vector must not carry a list-0 reference.");

            if (partition.MotionVectorL1 is H264MotionVector vector1)
            {
                if (partition.ReferenceIndexL1 is not int referenceIndex1 ||
                    (uint)referenceIndex1 >= (uint)referenceList1.Count)
                    throw new FormatException("The reconstructed B partition has an invalid list-1 reference.");
                prediction1 = _motionCompensator.Predict(
                    referenceList1[referenceIndex1],
                    checked(partition.X * 4), checked(partition.Y * 4),
                    checked(partition.Width * 4), checked(partition.Height * 4),
                    vector1);
            }
            else if (partition.ReferenceIndexL1 is not null)
                throw new FormatException("A B partition without a list-1 vector must not carry a list-1 reference.");

            H264MotionCompensatedBlock combined = (prediction0, prediction1) switch
            {
                (not null, null) => prediction0,
                (null, not null) => prediction1,
                (not null, not null) => CombineBiPrediction(
                    prediction0, prediction1, partition, referencePictureLists,
                    currentPictureOrderCount, weightedBipredIdc,
                    predictionWeights, chromaFormatIdc),
                _ => throw new FormatException("A reconstructed B partition must use list 0, list 1, or both.")
            };
            result.Add(new H264BInterMacroblockPrediction(partition, combined));
        }

        return result;
    }

    public static H264MotionCompensatedBlock Assemble(
        IReadOnlyList<H264BInterMacroblockPrediction> predictions,
        uint chromaFormatIdc)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        if (chromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("B prediction assembly supports 4:2:0 and 4:2:2 only.");
        if (predictions.Count == 0)
            throw new ArgumentException("At least one B partition prediction is required.", nameof(predictions));

        int chromaHeight = chromaFormatIdc == 1 ? 8 : 16;
        byte[] luma = new byte[256];
        byte[] cb = new byte[8 * chromaHeight];
        byte[] cr = new byte[8 * chromaHeight];
        bool[] lumaWritten = new bool[256];
        bool[] chromaWritten = new bool[8 * chromaHeight];
        int chromaScaleY = chromaFormatIdc == 1 ? 2 : 4;

        foreach (H264BInterMacroblockPrediction item in predictions)
        {
            if (item is null || item.Partition is null || item.Prediction is null)
                throw new ArgumentException("B partition predictions cannot contain null entries.", nameof(predictions));
            H264ReconstructedBInterSubPartition partition = item.Partition;
            H264MotionCompensatedBlock block = item.Prediction;
            ValidatePartition(partition);
            int width = checked(partition.Width * 4);
            int height = checked(partition.Height * 4);
            int chromaWidth = checked(partition.Width * 2);
            int blockChromaHeight = checked(partition.Height * chromaScaleY);
            if (block.LumaWidth != width || block.LumaHeight != height ||
                block.ChromaWidth != chromaWidth || block.ChromaHeight != blockChromaHeight ||
                block.Luma.Length != width * height ||
                block.Cb.Length != chromaWidth * blockChromaHeight ||
                block.Cr.Length != chromaWidth * blockChromaHeight)
                throw new FormatException("A B prediction buffer does not match its sub-partition geometry.");

            int originX = partition.X * 4, originY = partition.Y * 4;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int target = (originY + y) * 16 + originX + x;
                if (lumaWritten[target])
                    throw new FormatException("B prediction sub-partitions overlap in the luma plane.");
                lumaWritten[target] = true;
                luma[target] = block.Luma[y * width + x];
            }

            int chromaOriginX = partition.X * 2;
            int chromaOriginY = partition.Y * chromaScaleY;
            for (int y = 0; y < blockChromaHeight; y++)
            for (int x = 0; x < chromaWidth; x++)
            {
                int target = (chromaOriginY + y) * 8 + chromaOriginX + x;
                if (chromaWritten[target])
                    throw new FormatException("B prediction sub-partitions overlap in a chroma plane.");
                chromaWritten[target] = true;
                cb[target] = block.Cb[y * chromaWidth + x];
                cr[target] = block.Cr[y * chromaWidth + x];
            }
        }

        if (lumaWritten.Any(value => !value) || chromaWritten.Any(value => !value))
            throw new FormatException("B prediction sub-partitions do not completely cover the macroblock.");
        return new H264MotionCompensatedBlock(16, 16, 8, chromaHeight, luma, cb, cr);
    }

    private static H264MotionCompensatedBlock CombineBiPrediction(
        H264MotionCompensatedBlock prediction0,
        H264MotionCompensatedBlock prediction1,
        H264ReconstructedBInterSubPartition partition,
        H264BReferencePictureLists references,
        int currentPoc,
        byte weightedBipredIdc,
        H264PredictionWeightTable? weights,
        uint chromaFormatIdc)
    {
        int index0 = partition.ReferenceIndexL0!.Value;
        int index1 = partition.ReferenceIndexL1!.Value;
        return weightedBipredIdc switch
        {
            0 => H264BiPredictionCombiner.CombineDefault(
                prediction0, prediction1, chromaFormatIdc),
            1 => H264BiPredictionCombiner.CombineExplicit(
                prediction0, prediction1, weights!, index0, index1, chromaFormatIdc),
            2 => H264BiPredictionCombiner.CombineImplicit(
                prediction0, prediction1, currentPoc,
                references.List0[index0].PictureOrderCount,
                references.List1[index1].PictureOrderCount,
                references.List0[index0].IsLongTerm,
                references.List1[index1].IsLongTerm,
                chromaFormatIdc),
            _ => throw new ArgumentOutOfRangeException(nameof(weightedBipredIdc))
        };
    }

    private static void ValidatePartition(H264ReconstructedBInterSubPartition partition)
    {
        if (partition.X < 0 || partition.Y < 0 ||
            partition.Width <= 0 || partition.Height <= 0 ||
            partition.X + partition.Width > 4 ||
            partition.Y + partition.Height > 4)
            throw new FormatException("A B sub-partition must lie within the 16x16 macroblock.");
    }
}
