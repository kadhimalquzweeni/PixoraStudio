using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reads CABAC residual coefficients for inter-coded P/B macroblocks and
/// returns the residual model consumed by the existing inter reconstructor.
/// </summary>
public sealed class H264CabacInterResidualMacroblockReader
{
    private static readonly (int X, int Y)[] LumaBlockScan =
    [
        (0, 0), (1, 0), (0, 1), (1, 1),
        (2, 0), (3, 0), (2, 1), (3, 1),
        (0, 2), (1, 2), (0, 3), (1, 3),
        (2, 2), (3, 2), (2, 3), (3, 3)
    ];

    private readonly H264CabacResidualBlockReader _residualReader;

    public H264CabacInterResidualMacroblockReader()
        : this(new H264CabacResidualBlockReader())
    {
    }

    public H264CabacInterResidualMacroblockReader(
        H264CabacResidualBlockReader residualReader)
    {
        _residualReader = residualReader ?? throw new ArgumentNullException(nameof(residualReader));
    }

    public H264InterResidualMacroblock Read(
        IH264CabacBinReader binReader,
        H264MacroblockHeader macroblockHeader,
        H264InterMacroblockResidualSyntax macroblockSyntax,
        H264SequenceParameterSet sequenceParameterSet,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264CabacResidualNeighbourContext neighbourContext)
    {
        ArgumentNullException.ThrowIfNull(binReader);
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(macroblockSyntax);
        ArgumentNullException.ThrowIfNull(sequenceParameterSet);
        ArgumentNullException.ThrowIfNull(neighbourContext);
        ValidatePosition(macroblockX, macroblockY, sliceId);

        if (macroblockHeader.IsIntra || macroblockHeader.IsSkipped || macroblockHeader.IsPcm)
            throw new ArgumentException(
                "The macroblock header must describe a coded inter macroblock.",
                nameof(macroblockHeader));
        if (!sequenceParameterSet.FrameMbsOnlyFlag ||
            sequenceParameterSet.SeparateColourPlaneFlag ||
            sequenceParameterSet.BitDepthLumaMinus8 != 0 ||
            sequenceParameterSet.BitDepthChromaMinus8 != 0 ||
            sequenceParameterSet.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException(
                "CABAC inter residual decoding supports frame-only 8-bit non-separate 4:2:0 and 4:2:2.");

        uint pictureWidthInMacroblocks = checked(sequenceParameterSet.PicWidthInMbsMinus1 + 1);
        uint pictureHeightInMacroblocks = checked(sequenceParameterSet.PicHeightInMapUnitsMinus1 + 1);
        if ((uint)macroblockX >= pictureWidthInMacroblocks ||
            (uint)macroblockY >= pictureHeightInMacroblocks)
            throw new ArgumentOutOfRangeException(nameof(macroblockX),
                "The macroblock coordinate is outside the SPS picture dimensions.");
        uint expectedAddress = checked((uint)macroblockY * pictureWidthInMacroblocks + (uint)macroblockX);
        if (macroblockHeader.MacroblockAddress != expectedAddress)
            throw new FormatException("The CABAC macroblock header address does not match its picture coordinate.");

        uint chromaArrayType = sequenceParameterSet.ChromaFormatIdc;
        if (macroblockSyntax.CodedBlockPattern.ChromaArrayType != chromaArrayType ||
            macroblockSyntax.CodedBlockPattern.Luma > 15 ||
            macroblockSyntax.CodedBlockPattern.Chroma > 2)
            throw new FormatException("The inter coded-block-pattern does not match the active chroma format.");

        uint codedLuma = macroblockSyntax.CodedBlockPattern.Luma;
        uint codedChroma = macroblockSyntax.CodedBlockPattern.Chroma;
        bool use8x8 = macroblockSyntax.TransformSize8X8Flag;
        int chromaBlocksHigh = chromaArrayType == 1 ? 2 : 4;

        for (int plane = 0; plane <= 5; plane++)
            neighbourContext.RecordMacroblock(
                plane, macroblockX, macroblockY, sliceId, isPcm: false);

        List<H264IntraLumaResidualBlock> lumaBlocks = [];
        List<H264Intra8x8LumaResidualBlock> lumaBlocks8x8 = [];
        int lumaBlockOriginX = checked(macroblockX * 4);
        int lumaBlockOriginY = checked(macroblockY * 4);

        if (use8x8)
        {
            (int X, int Y)[] blockPositions = [(0, 0), (1, 0), (0, 1), (1, 1)];
            H264CabacResidualBlockContextMap map =
                H264CabacResidualBlockContext.Create(5);

            for (int blockIndex = 0; blockIndex < 4; blockIndex++)
            {
                (int blockX, int blockY) = blockPositions[blockIndex];
                bool isCoded = (codedLuma & (1u << blockIndex)) != 0;
                int[] coefficients;
                if (isCoded)
                {
                    H264CavlcResidualBlock residual = _residualReader.ReadBlock(
                        binReader, map, codedBlockFlag: true);
                    coefficients = residual.Coefficients.ToArray();
                }
                else
                {
                    coefficients = new int[64];
                }

                lumaBlocks8x8.Add(new H264Intra8x8LumaResidualBlock(
                    blockX, blockY, isCoded, coefficients));

                for (int subY = 0; subY < 2; subY++)
                for (int subX = 0; subX < 2; subX++)
                {
                    int pictureX = lumaBlockOriginX + blockX * 2 + subX;
                    int pictureY = lumaBlockOriginY + blockY * 2 + subY;
                    neighbourContext.RecordBlock(
                        0, pictureX, pictureY, sliceId, isCoded);
                }
            }
        }
        else
        {
            H264CabacResidualBlockContextMap map =
                H264CabacResidualBlockContext.Create(2);

            foreach ((int localX, int localY) in LumaBlockScan)
            {
                int group = (localX >> 1) + ((localY >> 1) << 1);
                bool isGroupCoded = (codedLuma & (1u << group)) != 0;
                int pictureX = lumaBlockOriginX + localX;
                int pictureY = lumaBlockOriginY + localY;
                H264CavlcResidualBlock residual;

                if (isGroupCoded)
                {
                    int contextIncrement = neighbourContext.GetContextIndexIncrement(
                        0, pictureX, pictureY, 4, 4, sliceId,
                        currentMacroblockIsIntra: false);
                    residual = _residualReader.ReadBlockWithCodedBlockFlag(
                        binReader, map, contextIncrement);
                }
                else
                {
                    residual = EmptyBlock(map.MaximumCoefficients);
                }

                lumaBlocks.Add(new H264IntraLumaResidualBlock(
                    localX, localY, isGroupCoded, residual));
                neighbourContext.RecordBlock(
                    0, pictureX, pictureY, sliceId,
                    residual.TotalCoefficients != 0);
            }
        }

        List<H264IntraChromaResidualComponent> chromaComponents = [];
        H264CabacResidualBlockContextMap chromaDcMap =
            H264CabacResidualBlockContext.Create(3, chroma422: chromaArrayType == 2);
        H264CabacResidualBlockContextMap chromaAcMap =
            H264CabacResidualBlockContext.Create(4);
        int chromaBlockOriginX = checked(macroblockX * 2);
        int chromaBlockOriginY = checked(macroblockY * chromaBlocksHigh);
        H264CavlcResidualBlock?[] chromaDc = new H264CavlcResidualBlock?[2];

        // The syntax order is Cb DC, Cr DC, then Cb AC and Cr AC.
        if (codedChroma != 0)
        {
            for (int componentIndex = 0; componentIndex < 2; componentIndex++)
            {
                int dcPlane = componentIndex + 4;
                int contextIncrement = neighbourContext.GetContextIndexIncrement(
                    dcPlane, macroblockX, macroblockY, 1, 1, sliceId,
                    currentMacroblockIsIntra: false);
                chromaDc[componentIndex] = _residualReader.ReadBlockWithCodedBlockFlag(
                    binReader, chromaDcMap, contextIncrement);
                neighbourContext.RecordBlock(
                    dcPlane, macroblockX, macroblockY, sliceId,
                    chromaDc[componentIndex]!.TotalCoefficients != 0);
            }
        }

        for (byte component = 1; component <= 2; component++)
        {
            int plane = component;
            List<H264IntraChromaResidualBlock> acBlocks = [];
            for (int blockY = 0; blockY < chromaBlocksHigh; blockY++)
            for (int blockX = 0; blockX < 2; blockX++)
            {
                int pictureX = chromaBlockOriginX + blockX;
                int pictureY = chromaBlockOriginY + blockY;
                bool isGroupCoded = codedChroma == 2;
                H264CavlcResidualBlock residual;

                if (isGroupCoded)
                {
                    int contextIncrement = neighbourContext.GetContextIndexIncrement(
                        plane, pictureX, pictureY, 2, chromaBlocksHigh, sliceId,
                        currentMacroblockIsIntra: false);
                    residual = _residualReader.ReadBlockWithCodedBlockFlag(
                        binReader, chromaAcMap, contextIncrement);
                }
                else
                {
                    residual = EmptyBlock(chromaAcMap.MaximumCoefficients);
                }

                acBlocks.Add(new H264IntraChromaResidualBlock(
                    blockX, blockY, isGroupCoded, residual));
                neighbourContext.RecordBlock(
                    plane, pictureX, pictureY, sliceId,
                    residual.TotalCoefficients != 0);
            }

            chromaComponents.Add(new H264IntraChromaResidualComponent(
                component, chromaDc[component - 1], acBlocks));
        }

        return new H264InterResidualMacroblock(
            expectedAddress, lumaBlocks, chromaComponents)
        {
            TransformSize8X8Flag = use8x8,
            Luma8x8Blocks = lumaBlocks8x8
        };
    }

    private static H264CavlcResidualBlock EmptyBlock(int maximumCoefficients) =>
        new(maximumCoefficients, 0, 0, 0, new int[maximumCoefficients]);

    private static void ValidatePosition(int macroblockX, int macroblockY, int sliceId)
    {
        if (macroblockX < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (macroblockY < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
    }
}
