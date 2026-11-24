using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reads CABAC residual coefficients for frame-coded I_NxN and I_16x16
/// macroblocks. The returned model is shared with the existing reconstruction
/// pipeline; entropy parsing remains separate from pixel reconstruction.
/// </summary>
public sealed class H264CabacIntraResidualMacroblockReader
{
    private static readonly (int X, int Y)[] LumaBlockScan =
    [
        (0, 0), (1, 0), (0, 1), (1, 1),
        (2, 0), (3, 0), (2, 1), (3, 1),
        (0, 2), (1, 2), (0, 3), (1, 3),
        (2, 2), (3, 2), (2, 3), (3, 3)
    ];

    private readonly H264CabacResidualBlockReader _residualReader;

    public H264CabacIntraResidualMacroblockReader()
        : this(new H264CabacResidualBlockReader())
    {
    }

    public H264CabacIntraResidualMacroblockReader(
        H264CabacResidualBlockReader residualReader)
    {
        ArgumentNullException.ThrowIfNull(residualReader);
        _residualReader = residualReader;
    }

    public void RecordPcmMacroblock(
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264SequenceParameterSet sequenceParameterSet,
        H264CabacResidualNeighbourContext neighbourContext)
    {
        ArgumentNullException.ThrowIfNull(sequenceParameterSet);
        ArgumentNullException.ThrowIfNull(neighbourContext);
        ValidatePosition(macroblockX, macroblockY, sliceId);

        if (sequenceParameterSet.ChromaFormatIdc is not (1 or 2) ||
            sequenceParameterSet.SeparateColourPlaneFlag)
            throw new NotSupportedException("CABAC PCM neighbour tracking supports non-separate 4:2:0 and 4:2:2.");

        for (int plane = 0; plane <= 5; plane++)
            neighbourContext.RecordMacroblock(
                plane, macroblockX, macroblockY, sliceId, isPcm: true);
    }

    public H264IntraLumaResidualMacroblock Read(
        IH264CabacBinReader binReader,
        H264MacroblockHeader macroblockHeader,
        H264IntraMacroblockSyntax macroblockSyntax,
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

        if (!macroblockHeader.IsIntra || macroblockHeader.IsSkipped || macroblockHeader.IsPcm)
            throw new ArgumentException(
                "The macroblock header must describe a non-PCM intra macroblock.",
                nameof(macroblockHeader));
        if (!sequenceParameterSet.FrameMbsOnlyFlag ||
            sequenceParameterSet.SeparateColourPlaneFlag ||
            sequenceParameterSet.BitDepthLumaMinus8 != 0 ||
            sequenceParameterSet.BitDepthChromaMinus8 != 0 ||
            sequenceParameterSet.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException(
                "CABAC intra residual decoding supports frame-only 8-bit non-separate 4:2:0 and 4:2:2.");
        uint pictureWidthInMacroblocks = checked(sequenceParameterSet.PicWidthInMbsMinus1 + 1);
        uint pictureHeightInMacroblocks = checked(sequenceParameterSet.PicHeightInMapUnitsMinus1 + 1);
        if ((uint)macroblockX >= pictureWidthInMacroblocks ||
            (uint)macroblockY >= pictureHeightInMacroblocks)
            throw new ArgumentOutOfRangeException(nameof(macroblockX),
                "The macroblock coordinate is outside the SPS picture dimensions.");
        uint expectedAddress = checked((uint)macroblockY * pictureWidthInMacroblocks + (uint)macroblockX);
        if (macroblockHeader.MacroblockAddress != expectedAddress)
            throw new FormatException("The CABAC macroblock header address does not match its picture coordinate.");

        if (macroblockSyntax.CodedBlockPattern.ChromaArrayType != sequenceParameterSet.ChromaFormatIdc ||
            macroblockSyntax.CodedBlockPattern.Luma > 15 ||
            macroblockSyntax.CodedBlockPattern.Chroma > 2)
            throw new FormatException("The intra coded-block-pattern does not match the active chroma format.");
        if (macroblockHeader.IsIntra16x16 && macroblockSyntax.TransformSize8X8Flag.HasValue)
            throw new FormatException("I_16x16 macroblocks cannot contain transform_size_8x8_flag.");

        uint chromaArrayType = sequenceParameterSet.ChromaFormatIdc;
        int chromaBlocksHigh = chromaArrayType == 1 ? 2 : 4;
        uint codedLuma = macroblockSyntax.CodedBlockPattern.Luma;
        uint codedChroma = macroblockSyntax.CodedBlockPattern.Chroma;
        bool use8x8 = !macroblockHeader.IsIntra16x16 &&
            macroblockSyntax.TransformSize8X8Flag == true;

        for (int plane = 0; plane <= 5; plane++)
            neighbourContext.RecordMacroblock(
                plane, macroblockX, macroblockY, sliceId, isPcm: false);

        H264CavlcResidualBlock? intra16x16Dc = null;
        if (macroblockHeader.IsIntra16x16)
        {
            H264CabacResidualBlockContextMap dcMap =
                H264CabacResidualBlockContext.Create(0);
            int dcContextIncrement = neighbourContext.GetContextIndexIncrement(
                3, macroblockX, macroblockY, 1, 1, sliceId,
                currentMacroblockIsIntra: true);
            intra16x16Dc = _residualReader.ReadBlockWithCodedBlockFlag(
                binReader, dcMap, dcContextIncrement);
            neighbourContext.RecordBlock(
                3, macroblockX, macroblockY, sliceId,
                intra16x16Dc.TotalCoefficients != 0);
        }

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
            int category = macroblockHeader.IsIntra16x16 ? 1 : 2;
            H264CabacResidualBlockContextMap map =
                H264CabacResidualBlockContext.Create(category);

            foreach ((int localX, int localY) in LumaBlockScan)
            {
                int group = (localX >> 1) + ((localY >> 1) << 1);
                bool isCoded = (codedLuma & (1u << group)) != 0;
                int pictureX = lumaBlockOriginX + localX;
                int pictureY = lumaBlockOriginY + localY;
                H264CavlcResidualBlock residual;

                if (isCoded)
                {
                    int contextIncrement = neighbourContext.GetContextIndexIncrement(
                        0, pictureX, pictureY, 4, 4, sliceId,
                        currentMacroblockIsIntra: true);
                    residual = _residualReader.ReadBlockWithCodedBlockFlag(
                        binReader, map, contextIncrement);
                }
                else
                {
                    residual = EmptyBlock(map.MaximumCoefficients);
                }

                lumaBlocks.Add(new H264IntraLumaResidualBlock(
                    localX, localY, isCoded, residual));
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

        // residual syntax orders both chroma DC blocks before either
        // component's AC blocks. Keep this phase separate from the AC pass.
        if (codedChroma != 0)
        {
            for (int componentIndex = 0; componentIndex < 2; componentIndex++)
            {
                int plane = componentIndex + 1;
                int dcPlane = componentIndex + 4;
                int contextIncrement = neighbourContext.GetContextIndexIncrement(
                    dcPlane, macroblockX, macroblockY, 1, 1, sliceId,
                    currentMacroblockIsIntra: true);
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
                bool isCoded = codedChroma == 2;
                H264CavlcResidualBlock residual;

                if (isCoded)
                {
                    int contextIncrement = neighbourContext.GetContextIndexIncrement(
                        plane, pictureX, pictureY, 2, chromaBlocksHigh, sliceId,
                        currentMacroblockIsIntra: true);
                    residual = _residualReader.ReadBlockWithCodedBlockFlag(
                        binReader, chromaAcMap, contextIncrement);
                }
                else
                {
                    residual = EmptyBlock(chromaAcMap.MaximumCoefficients);
                }

                acBlocks.Add(new H264IntraChromaResidualBlock(
                    blockX, blockY, isCoded, residual));
                neighbourContext.RecordBlock(
                    plane, pictureX, pictureY, sliceId,
                    residual.TotalCoefficients != 0);
            }

            chromaComponents.Add(new H264IntraChromaResidualComponent(
                component, chromaDc[component - 1], acBlocks));
        }

        uint macroblockAddress = checked(
            (uint)(macroblockY * checked((int)sequenceParameterSet.PicWidthInMbsMinus1 + 1) +
                   macroblockX));

        return new H264IntraLumaResidualMacroblock(
            macroblockAddress, intra16x16Dc, lumaBlocks)
        {
            ChromaComponents = chromaComponents,
            Intra8x8Blocks = lumaBlocks8x8
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
