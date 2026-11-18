using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reads the CAVLC luma residual payload for monochrome I_NxN and I_16x16
/// macroblocks using picture-relative neighbour state.
/// Chroma residuals and Intra8x8 transform residual syntax are deliberately
/// rejected until their dedicated layouts are implemented.
/// </summary>
public sealed class H264IntraLumaResidualMacroblockReader
{
    private static readonly (int X, int Y)[] LumaBlockScan =
    [
        (0, 0), (1, 0), (0, 1), (1, 1),
        (2, 0), (3, 0), (2, 1), (3, 1),
        (0, 2), (1, 2), (0, 3), (1, 3),
        (2, 2), (3, 2), (2, 3), (3, 3)
    ];

    private readonly H264CavlcResidualBlockReader _residualReader;

    public H264IntraLumaResidualMacroblockReader()
        : this(new H264CavlcResidualBlockReader())
    {
    }

    public H264IntraLumaResidualMacroblockReader(
        H264CavlcResidualBlockReader residualReader)
    {
        ArgumentNullException.ThrowIfNull(residualReader);
        _residualReader = residualReader;
    }

    /// <summary>
    /// Reads luma residual syntax after the macroblock's prediction syntax,
    /// coded-block-pattern and mb_qp_delta have already been consumed.
    /// The supplied neighbour context must be retained across macroblocks
    /// in the same picture and reset at picture boundaries.
    /// </summary>
    public H264IntraLumaResidualMacroblock Read(
        H264BitReader reader,
        H264MacroblockHeader macroblockHeader,
        H264IntraMacroblockSyntax macroblockSyntax,
        H264SequenceParameterSet sequenceParameterSet,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264CavlcNeighbourContext neighbourContext)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(macroblockSyntax);
        ArgumentNullException.ThrowIfNull(sequenceParameterSet);
        ArgumentNullException.ThrowIfNull(neighbourContext);

        if (!macroblockHeader.IsIntra || macroblockHeader.IsSkipped ||
            macroblockHeader.IsPcm)
            throw new ArgumentException(
                "The macroblock header must describe a non-PCM intra macroblock.",
                nameof(macroblockHeader));

        if (macroblockX < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (macroblockY < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));

        if (sequenceParameterSet.ChromaFormatIdc != 0 ||
            sequenceParameterSet.SeparateColourPlaneFlag ||
            macroblockSyntax.CodedBlockPattern.Chroma != 0 ||
            macroblockSyntax.CodedBlockPattern.ChromaArrayType != 0)
            throw new NotSupportedException(
                "This residual reader currently supports monochrome luma residuals only.");

        if (macroblockSyntax.LumaPredictionModes?.Uses8X8Transform == true ||
            macroblockSyntax.TransformSize8X8Flag == true)
            throw new NotSupportedException(
                "Intra8x8 transform residual decoding is not implemented.");

        H264CavlcResidualBlock? intra16x16Dc = null;
        if (macroblockHeader.IsIntra16x16)
        {
            // Intra16x16 luma DC uses coeff_token with nC = 0 and is
            // transmitted before the sixteen luma AC blocks.
            intra16x16Dc = _residualReader.ReadBlock(reader, 0, 16);
        }

        int macroblockBlockX = checked(macroblockX * 4);
        int macroblockBlockY = checked(macroblockY * 4);
        uint codedPattern = macroblockSyntax.CodedBlockPattern.Luma;
        List<H264IntraLumaResidualBlock> blocks = new(16);

        foreach ((int localX, int localY) in LumaBlockScan)
        {
            int group = (localX >> 1) + ((localY >> 1) << 1);
            bool isCoded = (codedPattern & (1u << group)) != 0;
            int pictureX = checked(macroblockBlockX + localX);
            int pictureY = checked(macroblockBlockY + localY);
            int maximumCoefficients = macroblockHeader.IsIntra16x16 ? 15 : 16;

            H264CavlcResidualBlock residual;
            if (isCoded)
            {
                residual = neighbourContext.ReadBlock(
                    reader, pictureX, pictureY, sliceId, maximumCoefficients);
            }
            else
            {
                residual = new H264CavlcResidualBlock(
                    maximumCoefficients, 0, 0, 0,
                    new int[maximumCoefficients]);

                // Uncoded blocks still contribute TotalCoeff=0 to the
                // neighbour context used by later blocks and macroblocks.
                neighbourContext.RecordBlock(pictureX, pictureY, sliceId, 0);
            }

            blocks.Add(new H264IntraLumaResidualBlock(
                localX, localY, isCoded, residual));
        }

        return new H264IntraLumaResidualMacroblock(
            macroblockHeader.MacroblockAddress,
            intra16x16Dc,
            blocks);
    }
}
