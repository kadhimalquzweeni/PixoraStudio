using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Connects parsed P-slice inter macroblocks to list-0 reference selection
/// and fractional-sample motion compensation. It returns prediction blocks;
/// coded residuals and destination-picture commits are handled by later stages.
/// </summary>
public sealed class H264InterMacroblockPredictionDecoder
{
    private readonly H264InterMacroblockDecoder _macroblockDecoder;
    private readonly H264MotionCompensator _motionCompensator;

    public H264InterMacroblockPredictionDecoder()
        : this(new H264InterMacroblockDecoder(), new H264MotionCompensator())
    {
    }

    public H264InterMacroblockPredictionDecoder(
        H264InterMacroblockDecoder macroblockDecoder,
        H264MotionCompensator motionCompensator)
    {
        ArgumentNullException.ThrowIfNull(macroblockDecoder);
        ArgumentNullException.ThrowIfNull(motionCompensator);
        _macroblockDecoder = macroblockDecoder;
        _motionCompensator = motionCompensator;
    }

    /// <summary>
    /// Parses an inter macroblock, reconstructs its motion vectors and creates
    /// one prediction block per partition using the matching RefPicList0 entry.
    /// The reference list must be the active list after slice-header modification.
    /// </summary>
    public IReadOnlyList<H264InterMacroblockPrediction> DecodeAndPredict(
        H264BitReader reader,
        H264MacroblockHeader macroblockHeader,
        uint numRefIdxL0ActiveMinus1,
        IReadOnlyList<H264DecodedPictureSurface> referenceList0,
        H264MotionVectorPictureState motionVectorState,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264PredictionWeightTable? predictionWeights = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(referenceList0);
        ArgumentNullException.ThrowIfNull(motionVectorState);
        ValidateReferenceList(referenceList0, numRefIdxL0ActiveMinus1);

        H264InterMacroblockDecodeResult decoded = _macroblockDecoder.Decode(
            reader, macroblockHeader, numRefIdxL0ActiveMinus1,
            motionVectorState, macroblockX, macroblockY, sliceId);

        List<H264InterMacroblockPrediction> predictions =
            new(decoded.ReconstructedPartitions.Count);
        foreach (H264ReconstructedInterPartition partition in decoded.ReconstructedPartitions)
        {
            H264DecodedPictureSurface reference =
                referenceList0[partition.ReferenceIndex];
            H264MotionCompensatedBlock prediction = _motionCompensator.Predict(
                reference,
                checked(partition.X * 4),
                checked(partition.Y * 4),
                checked(partition.Width * 4),
                checked(partition.Height * 4),
                partition.MotionVector);
            prediction = H264WeightedPrediction.ApplyList0(
                prediction, predictionWeights, partition.ReferenceIndex,
                reference.ChromaFormatIdc);
            predictions.Add(new H264InterMacroblockPrediction(partition, prediction));
        }

        return predictions;
    }

    /// <summary>
    /// Derives a P_Skip vector and predicts the skipped macroblock from
    /// RefPicList0[0].
    /// </summary>
    public H264InterMacroblockPrediction DecodeSkippedAndPredict(
        H264MacroblockHeader macroblockHeader,
        IReadOnlyList<H264DecodedPictureSurface> referenceList0,
        H264MotionVectorPictureState motionVectorState,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264PredictionWeightTable? predictionWeights = null)
    {
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(referenceList0);
        ArgumentNullException.ThrowIfNull(motionVectorState);
        if (referenceList0.Count == 0)
            throw new ArgumentException("RefPicList0 must contain at least one reference picture.", nameof(referenceList0));

        H264ReconstructedInterPartition partition = _macroblockDecoder.DecodeSkipped(
            macroblockHeader, motionVectorState, macroblockX, macroblockY, sliceId);
        H264MotionCompensatedBlock prediction = _motionCompensator.Predict(
            referenceList0[0],
            checked(partition.X * 4),
            checked(partition.Y * 4),
            checked(partition.Width * 4),
            checked(partition.Height * 4),
            partition.MotionVector);

        prediction = H264WeightedPrediction.ApplyList0(
            prediction, predictionWeights, partition.ReferenceIndex,
            referenceList0[0].ChromaFormatIdc);
        return new H264InterMacroblockPrediction(partition, prediction);
    }

    private static void ValidateReferenceList(
        IReadOnlyList<H264DecodedPictureSurface> referenceList0,
        uint numRefIdxL0ActiveMinus1)
    {
        if (numRefIdxL0ActiveMinus1 > 31)
            throw new ArgumentOutOfRangeException(nameof(numRefIdxL0ActiveMinus1));
        int activeCount = checked((int)(numRefIdxL0ActiveMinus1 + 1));
        if (referenceList0.Count != activeCount)
            throw new ArgumentException(
                "The supplied reference list must contain exactly the active RefPicList0 entries.",
                nameof(referenceList0));
        for (int i = 0; i < referenceList0.Count; i++)
            if (referenceList0[i] is null)
                throw new ArgumentException("RefPicList0 cannot contain null reference surfaces.", nameof(referenceList0));
    }
}
