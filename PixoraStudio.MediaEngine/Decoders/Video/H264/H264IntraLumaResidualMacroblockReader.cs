using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reads CAVLC luma and 4:2:0/4:2:2 chroma residual payloads for
/// frame-only I_NxN and I_16x16 macroblocks using picture-relative state,
/// including the four-list CAVLC representation of each Intra8x8 block.
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
        H264CavlcNeighbourContext neighbourContext,
        H264CavlcNeighbourContext? chromaCbNeighbourContext = null,
        H264CavlcNeighbourContext? chromaCrNeighbourContext = null)
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

        if (!sequenceParameterSet.FrameMbsOnlyFlag)
            throw new NotSupportedException(
                "This residual reader currently supports frame-only pictures.");

        uint pictureWidthInMacroblocks = checked(sequenceParameterSet.PicWidthInMbsMinus1 + 1);
        uint pictureHeightInMacroblocks = checked(sequenceParameterSet.PicHeightInMapUnitsMinus1 + 1);
        if ((uint)macroblockX >= pictureWidthInMacroblocks)
            throw new ArgumentOutOfRangeException(nameof(macroblockX),
                "The macroblock X coordinate is outside the picture.");
        if ((uint)macroblockY >= pictureHeightInMacroblocks)
            throw new ArgumentOutOfRangeException(nameof(macroblockY),
                "The macroblock Y coordinate is outside the picture.");

        if (macroblockSyntax.CodedBlockPattern.Luma > 15)
            throw new FormatException("The luma coded-block-pattern value must be in the range 0..15.");

        if (sequenceParameterSet.SeparateColourPlaneFlag)
            throw new NotSupportedException(
                "Separate-colour-plane residual decoding is not implemented.");

        uint chromaArrayType = sequenceParameterSet.ChromaFormatIdc;
        if (chromaArrayType is not (0 or 1 or 2))
            throw new NotSupportedException(
                "This residual reader supports monochrome, 4:2:0 and 4:2:2 only.");

        uint chromaPattern = macroblockSyntax.CodedBlockPattern.Chroma;
        if (chromaArrayType == 0)
        {
            if (chromaPattern != 0 ||
                macroblockSyntax.CodedBlockPattern.ChromaArrayType != 0)
                throw new FormatException(
                    "Monochrome macroblocks cannot contain chroma coded-block-pattern data.");
        }
        else
        {
            if (macroblockSyntax.CodedBlockPattern.ChromaArrayType != chromaArrayType)
                throw new FormatException(
                    "The coded-block-pattern ChromaArrayType does not match the SPS.");
            if (chromaPattern > 2)
                throw new FormatException(
                    "The chroma coded-block-pattern value must be in the range 0..2.");
            ArgumentNullException.ThrowIfNull(chromaCbNeighbourContext);
            ArgumentNullException.ThrowIfNull(chromaCrNeighbourContext);
            if (ReferenceEquals(neighbourContext, chromaCbNeighbourContext) ||
                ReferenceEquals(neighbourContext, chromaCrNeighbourContext) ||
                ReferenceEquals(chromaCbNeighbourContext, chromaCrNeighbourContext))
                throw new ArgumentException(
                    "Luma, Cb and Cr residuals require independent neighbour contexts.");
        }

        bool use8x8 = !macroblockHeader.IsIntra16x16 &&
            macroblockSyntax.TransformSize8X8Flag == true;
        if (macroblockHeader.IsIntra16x16 && macroblockSyntax.TransformSize8X8Flag.HasValue)
            throw new FormatException("I_16x16 macroblocks cannot contain transform_size_8x8_flag.");
        H264IntraPredictionModeSyntax? predictionModes = macroblockSyntax.LumaPredictionModes;
        if (use8x8 &&
            (predictionModes is null || !predictionModes.Uses8X8Transform ||
             predictionModes.Modes.Count != 4))
            throw new FormatException("Intra8x8 residual syntax requires four Intra8x8 prediction-mode entries.");
        if (!use8x8 && macroblockSyntax.LumaPredictionModes?.Uses8X8Transform == true)
            throw new FormatException("Intra8x8 prediction-mode syntax requires transform_size_8x8_flag.");

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
        List<H264Intra8x8LumaResidualBlock> blocks8x8 = new(4);

        if (use8x8)
        {
            if (macroblockSyntax.LumaPredictionModes?.Modes.Count != 4)
                throw new FormatException("An Intra8x8 macroblock must contain four prediction-mode entries.");

            (int X, int Y)[] blockPositions = [(0, 0), (1, 0), (0, 1), (1, 1)];
            for (int blockIndex = 0; blockIndex < 4; blockIndex++)
            {
                (int blockX, int blockY) = blockPositions[blockIndex];
                bool isCoded = (codedPattern & (1u << blockIndex)) != 0;
                int[] combinedScan = new int[64];

                for (int subBlock = 0; subBlock < 4; subBlock++)
                {
                    int subX = subBlock & 1;
                    int subY = subBlock >> 1;
                    int localX = blockX * 2 + subX;
                    int localY = blockY * 2 + subY;
                    int pictureX = checked(macroblockBlockX + localX);
                    int pictureY = checked(macroblockBlockY + localY);

                    H264CavlcResidualBlock subResidual;
                    if (isCoded)
                    {
                        subResidual = neighbourContext.ReadBlock(
                            reader, pictureX, pictureY, sliceId, 16);
                    }
                    else
                    {
                        subResidual = new H264CavlcResidualBlock(16, 0, 0, 0, new int[16]);
                        neighbourContext.RecordBlock(pictureX, pictureY, sliceId, 0);
                    }

                    // CAVLC represents an 8x8 block as four 4x4 coefficient
                    // lists. Every list contributes every fourth 8x8 scan level.
                    for (int coefficient = 0; coefficient < 16; coefficient++)
                        combinedScan[4 * coefficient + subBlock] =
                            subResidual.Coefficients[coefficient];
                }

                blocks8x8.Add(new H264Intra8x8LumaResidualBlock(
                    blockX, blockY, isCoded, combinedScan));
            }
        }
        else
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

        List<H264IntraChromaResidualComponent> chromaComponents = [];
        if (chromaArrayType is 1 or 2)
        {
            int chromaBlocksHigh = chromaArrayType == 1 ? 2 : 4;
            int chromaBlocksPerComponent = 2 * chromaBlocksHigh;
            int maximumDcCoefficients = chromaArrayType == 1 ? 4 : 8;
            int chromaMacroblockBlockX = checked(macroblockX * 2);
            int chromaMacroblockBlockY = checked(macroblockY * chromaBlocksHigh);

            // H.264 residual syntax sends both chroma DC blocks first
            // (Cb, then Cr), followed by the Cb AC blocks and then Cr AC.
            H264CavlcResidualBlock? cbDc = chromaPattern == 0
                ? null
                : _residualReader.ReadBlock(reader, 0, maximumDcCoefficients);
            H264CavlcResidualBlock? crDc = chromaPattern == 0
                ? null
                : _residualReader.ReadBlock(reader, 0, maximumDcCoefficients);

            chromaComponents.Add(ReadChromaAcComponent(
                reader, 1, cbDc, chromaPattern,
                chromaBlocksHigh, chromaBlocksPerComponent,
                chromaMacroblockBlockX, chromaMacroblockBlockY,
                sliceId, chromaCbNeighbourContext!));

            chromaComponents.Add(ReadChromaAcComponent(
                reader, 2, crDc, chromaPattern,
                chromaBlocksHigh, chromaBlocksPerComponent,
                chromaMacroblockBlockX, chromaMacroblockBlockY,
                sliceId, chromaCrNeighbourContext!));
        }

        return new H264IntraLumaResidualMacroblock(
            macroblockHeader.MacroblockAddress,
            intra16x16Dc,
            blocks)
        {
            ChromaComponents = chromaComponents,
            Intra8x8Blocks = blocks8x8
        };
    }
    private static H264IntraChromaResidualComponent ReadChromaAcComponent(
        H264BitReader reader,
        byte componentId,
        H264CavlcResidualBlock? dc,
        uint chromaPattern,
        int blocksHigh,
        int blocksPerComponent,
        int macroblockBlockX,
        int macroblockBlockY,
        int sliceId,
        H264CavlcNeighbourContext neighbourContext)
    {
        // DC has already been decoded for both components. Decode AC now
        // in component order, using the component-specific neighbour state.
        List<H264IntraChromaResidualBlock> blocks = new(blocksPerComponent);
        for (int y = 0; y < blocksHigh; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                bool isCoded = chromaPattern == 2;
                int pictureX = checked(macroblockBlockX + x);
                int pictureY = checked(macroblockBlockY + y);
                H264CavlcResidualBlock residual;

                if (isCoded)
                {
                    residual = neighbourContext.ReadBlock(
                        reader, pictureX, pictureY, sliceId, 15);
                }
                else
                {
                    residual = new H264CavlcResidualBlock(
                        15, 0, 0, 0, new int[15]);
                    neighbourContext.RecordBlock(pictureX, pictureY, sliceId, 0);
                }

                blocks.Add(new H264IntraChromaResidualBlock(
                    x, y, isCoded, residual));
            }
        }

        return new H264IntraChromaResidualComponent(componentId, dc, blocks);
    }

}
