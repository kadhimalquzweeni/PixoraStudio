using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parses CABAC prediction, coded-block-pattern and QP-delta syntax for
/// I_NxN and I_16x16 macroblocks.
/// </summary>
public sealed class H264CabacIntraMacroblockSyntaxReader
{
    private readonly H264IntraPredictionModeReader _predictionModeReader = new();

    public H264IntraMacroblockSyntax Read(
        H264CabacSyntaxElementReader reader,
        H264MacroblockHeader macroblockHeader,
        H264SequenceParameterSet sequenceParameterSet,
        H264PictureParameterSetHeader pictureParameterSet,
        int chromaPredictionContextIncrement,
        IReadOnlyList<int>? lumaCbpContextIncrements,
        IReadOnlyList<int>? chromaCbpContextIncrements,
        int qpDeltaContextIncrement)
    {
        return ReadCore(
            reader, macroblockHeader, sequenceParameterSet, pictureParameterSet,
            chromaPredictionContextIncrement, lumaCbpContextIncrements,
            chromaCbpContextIncrements, qpDeltaContextIncrement, 0, null);
    }

    /// <summary>
    /// Parses CABAC intra syntax while deriving neighbour context increments
    /// from retained picture state. Luma CBP increments are recalculated after
    /// every bin so the current macroblock's earlier CBP bins affect later bins.
    /// </summary>
    public H264IntraMacroblockSyntax Read(
        H264CabacSyntaxElementReader reader,
        H264MacroblockHeader macroblockHeader,
        H264SequenceParameterSet sequenceParameterSet,
        H264PictureParameterSetHeader pictureParameterSet,
        H264CabacIntraMacroblockNeighbourContext neighbourContext,
        int macroblockX,
        int macroblockY,
        int sliceId,
        uint? previousMacroblockAddress)
    {
        ArgumentNullException.ThrowIfNull(neighbourContext);
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(sequenceParameterSet);

        uint chromaArrayType = sequenceParameterSet.SeparateColourPlaneFlag
            ? 0u
            : sequenceParameterSet.ChromaFormatIdc;
        int chromaPredictionIncrement = chromaArrayType == 0
            ? 0
            : neighbourContext.GetChromaPredictionModeContextIncrement(
                macroblockX, macroblockY, sliceId);

        IReadOnlyList<int>? chromaCbpIncrements = null;
        if (!macroblockHeader.IsIntra16x16 && chromaArrayType is 1 or 2)
        {
            chromaCbpIncrements =
                neighbourContext.GetChromaCodedBlockPatternContextIncrements(
                    macroblockX, macroblockY, sliceId);
        }

        int qpDeltaIncrement = neighbourContext.GetQpDeltaContextIncrement(
            previousMacroblockAddress, sliceId);
        int transformSizeContextIncrement = neighbourContext.GetTransformSize8x8ContextIncrement(
            macroblockX, macroblockY, sliceId);
        Func<int, uint, int>? lumaCbpContextProvider = null;
        if (!macroblockHeader.IsIntra16x16)
        {
            lumaCbpContextProvider = (binIndex, decodedPattern) =>
                neighbourContext.GetLumaCodedBlockPatternContextIncrement(
                    macroblockX, macroblockY, sliceId, binIndex, decodedPattern);
        }

        return ReadCore(
            reader, macroblockHeader, sequenceParameterSet, pictureParameterSet,
            chromaPredictionIncrement, null, chromaCbpIncrements,
            qpDeltaIncrement, transformSizeContextIncrement, lumaCbpContextProvider);
    }

    private H264IntraMacroblockSyntax ReadCore(
        H264CabacSyntaxElementReader reader,
        H264MacroblockHeader macroblockHeader,
        H264SequenceParameterSet sequenceParameterSet,
        H264PictureParameterSetHeader pictureParameterSet,
        int chromaPredictionContextIncrement,
        IReadOnlyList<int>? lumaCbpContextIncrements,
        IReadOnlyList<int>? chromaCbpContextIncrements,
        int qpDeltaContextIncrement,
        int transformSizeContextIncrement,
        Func<int, uint, int>? lumaCbpContextProvider)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(sequenceParameterSet);
        ArgumentNullException.ThrowIfNull(pictureParameterSet);

        if (!pictureParameterSet.EntropyCodingModeFlag)
            throw new ArgumentException("The supplied PPS must enable CABAC.", nameof(pictureParameterSet));
        if (pictureParameterSet.SequenceParameterSetId != sequenceParameterSet.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pictureParameterSet));
        if (!macroblockHeader.IsIntra || macroblockHeader.IsSkipped || macroblockHeader.IsPcm)
            throw new ArgumentException(
                "The macroblock header must describe a non-PCM intra macroblock.",
                nameof(macroblockHeader));

        uint chromaArrayType = sequenceParameterSet.SeparateColourPlaneFlag
            ? 0u
            : sequenceParameterSet.ChromaFormatIdc;
        if (chromaArrayType > 3)
            throw new FormatException("The SPS contains an invalid ChromaArrayType.");

        bool isIntraNxN = !macroblockHeader.IsIntra16x16;
        if (isIntraNxN)
        {
            if (lumaCbpContextProvider is null)
            {
                if (lumaCbpContextIncrements is null)
                    throw new ArgumentNullException(nameof(lumaCbpContextIncrements));
                if (lumaCbpContextIncrements.Count != 4 ||
                    lumaCbpContextIncrements.Any(value => value is < 0 or > 3))
                    throw new ArgumentException(
                        "Four luma CBP context increments in the range 0..3 are required.",
                        nameof(lumaCbpContextIncrements));
            }

            if (chromaArrayType is 1 or 2 &&
                (chromaCbpContextIncrements is null ||
                 chromaCbpContextIncrements.Count != 2 ||
                 chromaCbpContextIncrements.Any(value => value is < 0 or > 3)))
                throw new ArgumentException(
                    "Two chroma CBP context increments in the range 0..3 are required.",
                    nameof(chromaCbpContextIncrements));
        }

        if (chromaArrayType != 0 && (chromaPredictionContextIncrement is < 0 or > 2))
            throw new ArgumentOutOfRangeException(nameof(chromaPredictionContextIncrement));
        if (qpDeltaContextIncrement is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(qpDeltaContextIncrement));
        if (transformSizeContextIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(transformSizeContextIncrement));
        uint intra16LumaCbp = 0;
        uint intra16ChromaCbp = 0;
        if (macroblockHeader.IsIntra16x16)
        {
            if (macroblockHeader.CodedBlockPatternLuma is not uint lumaCbp ||
                macroblockHeader.CodedBlockPatternChroma is not uint chromaCbp)
                throw new FormatException("I_16x16 macroblock header is missing its coded block pattern.");

            intra16LumaCbp = lumaCbp;
            intra16ChromaCbp = chromaCbp;
        }

        bool? transform8X8 = null;
        H264IntraPredictionModeSyntax? predictionModes = null;
        if (isIntraNxN)
        {
            if (pictureParameterSet.Transform8X8ModeFlag)
                transform8X8 = reader.ReadTransformSize8x8Flag(transformSizeContextIncrement);

            predictionModes = _predictionModeReader.Read(
                reader, transform8X8 == true);
        }

        uint? chromaPredictionMode = null;
        if (chromaArrayType != 0)
        {
            chromaPredictionMode = checked((uint)reader.ReadIntraChromaPredictionMode(
                chromaPredictionContextIncrement));
        }

        H264CodedBlockPattern codedBlockPattern;
        if (macroblockHeader.IsIntra16x16)
        {
            codedBlockPattern = new H264CodedBlockPattern(
                macroblockHeader.MbTypeCode,
                intra16LumaCbp,
                intra16ChromaCbp,
                chromaArrayType);
        }
        else if (lumaCbpContextProvider is not null)
        {
            codedBlockPattern = reader.ReadIntraCodedBlockPattern(
                lumaCbpContextProvider, chromaCbpContextIncrements, chromaArrayType);
        }
        else
        {
            codedBlockPattern = reader.ReadIntraCodedBlockPattern(
                lumaCbpContextIncrements!, chromaCbpContextIncrements, chromaArrayType);
        }

        int? macroblockQpDelta = null;
        if (codedBlockPattern.Luma != 0 ||
            codedBlockPattern.Chroma != 0 ||
            macroblockHeader.IsIntra16x16)
        {
            macroblockQpDelta = reader.ReadMacroblockQpDelta(qpDeltaContextIncrement);
        }

        return new H264IntraMacroblockSyntax(
            transform8X8,
            predictionModes,
            chromaPredictionMode,
            codedBlockPattern,
            macroblockQpDelta);
    }
}
