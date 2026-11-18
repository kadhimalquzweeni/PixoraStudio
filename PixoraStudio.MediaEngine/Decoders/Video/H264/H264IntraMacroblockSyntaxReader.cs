using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parses the remaining prediction, coded-block-pattern and QP-delta syntax
/// for intra I_NxN and I_16x16 macroblocks in CAVLC streams.
/// </summary>
public sealed class H264IntraMacroblockSyntaxReader
{
    private readonly H264IntraPredictionModeReader _predictionModeReader = new();
    private readonly H264CodedBlockPatternReader _codedBlockPatternReader = new();

    public H264IntraMacroblockSyntax Read(
        H264BitReader reader,
        H264MacroblockHeader macroblockHeader,
        H264SequenceParameterSet sequenceParameterSet,
        H264PictureParameterSetHeader pictureParameterSet)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(sequenceParameterSet);
        ArgumentNullException.ThrowIfNull(pictureParameterSet);

        if (!macroblockHeader.IsIntra || macroblockHeader.IsSkipped || macroblockHeader.IsPcm)
            throw new ArgumentException("The macroblock header must describe a non-PCM intra macroblock.", nameof(macroblockHeader));
        if (pictureParameterSet.EntropyCodingModeFlag)
            throw new NotSupportedException("CABAC intra macroblock syntax is not implemented.");
        if (pictureParameterSet.SequenceParameterSetId != sequenceParameterSet.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pictureParameterSet));

        uint chromaArrayType = sequenceParameterSet.SeparateColourPlaneFlag
            ? 0u
            : sequenceParameterSet.ChromaFormatIdc;
        if (chromaArrayType > 3)
            throw new FormatException("The SPS contains an invalid ChromaArrayType.");

        bool isIntraNxN = !macroblockHeader.IsIntra16x16;
        bool? transform8X8 = null;
        H264IntraPredictionModeSyntax? predictionModes = null;

        if (isIntraNxN)
        {
            if (pictureParameterSet.Transform8X8ModeFlag)
                transform8X8 = reader.ReadFlag();

            predictionModes = _predictionModeReader.Read(
                reader, transform8X8 == true);
        }

        uint? chromaPredictionMode = null;
        if (chromaArrayType != 0)
        {
            chromaPredictionMode = reader.ReadUnsignedExpGolomb();
            if (chromaPredictionMode > 3)
                throw new FormatException("intra_chroma_pred_mode must be in the range 0..3.");
        }

        H264CodedBlockPattern codedBlockPattern;
        if (macroblockHeader.IsIntra16x16)
        {
            if (macroblockHeader.CodedBlockPatternLuma is null ||
                macroblockHeader.CodedBlockPatternChroma is null)
                throw new FormatException("I_16x16 macroblock header is missing its coded block pattern.");

            codedBlockPattern = new H264CodedBlockPattern(
                macroblockHeader.MbTypeCode,
                macroblockHeader.CodedBlockPatternLuma.Value,
                macroblockHeader.CodedBlockPatternChroma.Value,
                chromaArrayType);
        }
        else
        {
            codedBlockPattern = _codedBlockPatternReader.ReadIntra(reader, chromaArrayType);
        }

        int? macroblockQpDelta = null;
        if (codedBlockPattern.Luma != 0 ||
            codedBlockPattern.Chroma != 0 ||
            macroblockHeader.IsIntra16x16)
        {
            macroblockQpDelta = reader.ReadSignedExpGolomb();
        }

        return new H264IntraMacroblockSyntax(
            transform8X8,
            predictionModes,
            chromaPredictionMode,
            codedBlockPattern,
            macroblockQpDelta);
    }
}
