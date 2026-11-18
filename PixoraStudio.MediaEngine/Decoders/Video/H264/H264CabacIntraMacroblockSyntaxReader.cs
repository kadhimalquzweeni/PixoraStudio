using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parses CABAC prediction, coded-block-pattern and QP-delta syntax for
/// I_NxN and I_16x16 macroblocks. Context increments are supplied by the
/// picture-level neighbour-state tracker.
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
            if (lumaCbpContextIncrements is null)
                throw new ArgumentNullException(nameof(lumaCbpContextIncrements));
            if (lumaCbpContextIncrements.Count != 4 ||
                lumaCbpContextIncrements.Any(value => value is < 0 or > 3))
                throw new ArgumentException(
                    "Four luma CBP context increments in the range 0..3 are required.",
                    nameof(lumaCbpContextIncrements));
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
        if (qpDeltaContextIncrement is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(qpDeltaContextIncrement));
        if (macroblockHeader.IsIntra16x16 &&
            (macroblockHeader.CodedBlockPatternLuma is null ||
             macroblockHeader.CodedBlockPatternChroma is null))
            throw new FormatException("I_16x16 macroblock header is missing its coded block pattern.");

        bool? transform8X8 = null;
        H264IntraPredictionModeSyntax? predictionModes = null;
        if (isIntraNxN)
        {
            if (pictureParameterSet.Transform8X8ModeFlag)
                transform8X8 = reader.ReadTransformSize8x8Flag();

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
                macroblockHeader.CodedBlockPatternLuma.Value,
                macroblockHeader.CodedBlockPatternChroma.Value,
                chromaArrayType);
        }
        else
        {
            codedBlockPattern = reader.ReadIntraCodedBlockPattern(
                lumaCbpContextIncrements, chromaCbpContextIncrements, chromaArrayType);
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
