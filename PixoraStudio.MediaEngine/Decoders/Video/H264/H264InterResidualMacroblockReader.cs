using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reads the CAVLC residual() payload for frame-coded inter macroblocks in
/// 8-bit non-separate 4:2:0/4:2:2 pictures. The caller supplies the already
/// decoded coded_block_pattern and maintains the three neighbour contexts
/// across macroblocks and slices in the same picture.
/// </summary>
public sealed class H264InterResidualMacroblockReader
{
    private static readonly (int X, int Y)[] LumaBlockScan =
    [
        (0, 0), (1, 0), (0, 1), (1, 1),
        (2, 0), (3, 0), (2, 1), (3, 1),
        (0, 2), (1, 2), (0, 3), (1, 3),
        (2, 2), (3, 2), (2, 3), (3, 3)
    ];

    private readonly H264CavlcResidualBlockReader _residualReader;

    public H264InterResidualMacroblockReader()
        : this(new H264CavlcResidualBlockReader())
    {
    }

    public H264InterResidualMacroblockReader(H264CavlcResidualBlockReader residualReader)
    {
        ArgumentNullException.ThrowIfNull(residualReader);
        _residualReader = residualReader;
    }

    /// <summary>
    /// Reads inter residual coefficients after inter prediction syntax,
    /// coded_block_pattern and (when required) mb_qp_delta have been consumed.
    /// transform_size_8x8_flag is not supported by this first CAVLC path.
    /// </summary>
    public H264InterResidualMacroblock Read(
        H264BitReader reader,
        H264MacroblockHeader header,
        H264CodedBlockPattern codedBlockPattern,
        H264SequenceParameterSet sps,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264CavlcNeighbourContext lumaNeighbours,
        H264CavlcNeighbourContext chromaCbNeighbours,
        H264CavlcNeighbourContext chromaCrNeighbours,
        bool transformSize8x8Flag = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(codedBlockPattern);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(lumaNeighbours);
        ArgumentNullException.ThrowIfNull(chromaCbNeighbours);
        ArgumentNullException.ThrowIfNull(chromaCrNeighbours);

        if (header.IsIntra || header.IsSkipped || header.IsPcm)
            throw new ArgumentException("The macroblock header must describe a coded inter macroblock.", nameof(header));
        if (transformSize8x8Flag && sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Inter 8x8 transform residual decoding supports 4:2:0 and 4:2:2 only.");
        if (!sps.FrameMbsOnlyFlag || sps.SeparateColourPlaneFlag ||
            sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0 ||
            sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Inter residual decoding supports frame-only 8-bit non-separate 4:2:0 and 4:2:2 only.");
        if (sps.PicWidthInMbsMinus1 + 1 > int.MaxValue ||
            sps.PicHeightInMapUnitsMinus1 + 1 > int.MaxValue)
            throw new FormatException("The SPS picture dimensions exceed supported coordinate limits.");

        int pictureWidth = checked((int)(sps.PicWidthInMbsMinus1 + 1));
        int pictureHeight = checked((int)(sps.PicHeightInMapUnitsMinus1 + 1));
        if ((uint)macroblockX >= (uint)pictureWidth)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if ((uint)macroblockY >= (uint)pictureHeight)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        uint expectedAddress = checked((uint)(macroblockY * pictureWidth + macroblockX));
        if (header.MacroblockAddress != expectedAddress)
            throw new FormatException("The macroblock header address does not match its picture coordinates.");
        if (codedBlockPattern.Luma > 15 || codedBlockPattern.Chroma > 2)
            throw new FormatException("The coded_block_pattern contains an out-of-range luma or chroma value.");
        if (codedBlockPattern.ChromaArrayType != sps.ChromaFormatIdc)
            throw new FormatException("The coded_block_pattern ChromaArrayType does not match the SPS.");
        if (ReferenceEquals(lumaNeighbours, chromaCbNeighbours) ||
            ReferenceEquals(lumaNeighbours, chromaCrNeighbours) ||
            ReferenceEquals(chromaCbNeighbours, chromaCrNeighbours))
            throw new ArgumentException("Luma, Cb and Cr residuals require independent CAVLC neighbour contexts.");

        int lumaOriginX = checked(macroblockX * 4);
        int lumaOriginY = checked(macroblockY * 4);
        List<H264IntraLumaResidualBlock> lumaBlocks = new(16);
        List<H264Intra8x8LumaResidualBlock> luma8x8Blocks = new(4);

        if (transformSize8x8Flag)
        {
            for (int blockIndex = 0; blockIndex < 4; blockIndex++)
            {
                int blockX = blockIndex & 1;
                int blockY = blockIndex >> 1;
                bool isCoded = (codedBlockPattern.Luma & (1u << blockIndex)) != 0;
                int[] combinedScan = new int[64];
                for (int subBlock = 0; subBlock < 4; subBlock++)
                {
                    int localX = blockX * 2 + (subBlock & 1);
                    int localY = blockY * 2 + (subBlock >> 1);
                    int pictureX = checked(lumaOriginX + localX);
                    int pictureY = checked(lumaOriginY + localY);
                    H264CavlcResidualBlock subResidual;
                    if (isCoded)
                        subResidual = lumaNeighbours.ReadBlock(
                            reader, pictureX, pictureY, sliceId, 16);
                    else
                    {
                        subResidual = EmptyBlock(16);
                        lumaNeighbours.RecordBlock(pictureX, pictureY, sliceId, 0);
                    }
                    for (int coefficient = 0; coefficient < 16; coefficient++)
                        combinedScan[4 * coefficient + subBlock] =
                            subResidual.Coefficients[coefficient];
                }
                luma8x8Blocks.Add(new H264Intra8x8LumaResidualBlock(
                    blockX, blockY, isCoded, combinedScan));
            }
        }
        else
        foreach ((int localX, int localY) in LumaBlockScan)
        {
            int group = (localX >> 1) + ((localY >> 1) << 1);
            bool isCoded = (codedBlockPattern.Luma & (1u << group)) != 0;
            int blockX = checked(lumaOriginX + localX);
            int blockY = checked(lumaOriginY + localY);
            H264CavlcResidualBlock residual;
            if (isCoded)
            {
                residual = lumaNeighbours.ReadBlock(reader, blockX, blockY, sliceId, 16);
            }
            else
            {
                residual = EmptyBlock(16);
                lumaNeighbours.RecordBlock(blockX, blockY, sliceId, 0);
            }

            lumaBlocks.Add(new H264IntraLumaResidualBlock(localX, localY, isCoded, residual));
        }

        int chromaBlocksHigh = sps.ChromaFormatIdc == 1 ? 2 : 4;
        int chromaBlocksPerComponent = chromaBlocksHigh * 2;
        int dcCapacity = chromaBlocksPerComponent;
        int chromaOriginX = checked(macroblockX * 2);
        int chromaOriginY = checked(macroblockY * chromaBlocksHigh);

        // Both DC blocks precede all AC blocks in residual() syntax.
        H264CavlcResidualBlock? cbDc = codedBlockPattern.Chroma == 0
            ? null
            : _residualReader.ReadBlock(reader, 0, dcCapacity);
        H264CavlcResidualBlock? crDc = codedBlockPattern.Chroma == 0
            ? null
            : _residualReader.ReadBlock(reader, 0, dcCapacity);

        H264IntraChromaResidualComponent cb = ReadChromaComponent(
            reader, 1, cbDc, codedBlockPattern.Chroma,
            chromaBlocksHigh, chromaOriginX, chromaOriginY, sliceId, chromaCbNeighbours);
        H264IntraChromaResidualComponent cr = ReadChromaComponent(
            reader, 2, crDc, codedBlockPattern.Chroma,
            chromaBlocksHigh, chromaOriginX, chromaOriginY, sliceId, chromaCrNeighbours);

        return new H264InterResidualMacroblock(header.MacroblockAddress, lumaBlocks, [cb, cr])
        {
            TransformSize8X8Flag = transformSize8x8Flag,
            Luma8x8Blocks = luma8x8Blocks
        };
    }

    private static H264IntraChromaResidualComponent ReadChromaComponent(
        H264BitReader reader,
        byte componentId,
        H264CavlcResidualBlock? dc,
        uint chromaPattern,
        int blocksHigh,
        int originX,
        int originY,
        int sliceId,
        H264CavlcNeighbourContext neighbours)
    {
        List<H264IntraChromaResidualBlock> blocks = new(blocksHigh * 2);
        for (int y = 0; y < blocksHigh; y++)
        for (int x = 0; x < 2; x++)
        {
            bool isCoded = chromaPattern == 2;
            int pictureX = checked(originX + x);
            int pictureY = checked(originY + y);
            H264CavlcResidualBlock residual;
            if (isCoded)
                residual = neighbours.ReadBlock(reader, pictureX, pictureY, sliceId, 15);
            else
            {
                residual = EmptyBlock(15);
                neighbours.RecordBlock(pictureX, pictureY, sliceId, 0);
            }
            blocks.Add(new H264IntraChromaResidualBlock(x, y, isCoded, residual));
        }
        return new H264IntraChromaResidualComponent(componentId, dc, blocks);
    }

    private static H264CavlcResidualBlock EmptyBlock(int capacity) =>
        new(capacity, 0, 0, 0, new int[capacity]);
}
