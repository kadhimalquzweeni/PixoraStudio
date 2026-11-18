using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parses list-0 prediction syntax for the five inter-coded P-slice macroblock
/// types. It deliberately does not predict motion vectors or reconstruct pixels.
/// </summary>
public sealed class H264InterMacroblockSyntaxReader
{
    public H264InterMacroblockSyntax Read(
        H264BitReader reader,
        H264MacroblockHeader macroblockHeader,
        uint numRefIdxL0ActiveMinus1)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(macroblockHeader);

        if (macroblockHeader.IsSkipped)
            throw new ArgumentException("Skipped macroblocks have no explicit inter-prediction syntax.", nameof(macroblockHeader));
        if (macroblockHeader.IsIntra)
            throw new ArgumentException("The macroblock header must describe an inter-coded P macroblock.", nameof(macroblockHeader));
        if (macroblockHeader.MbTypeCode > 4)
            throw new NotSupportedException("Only P_L0_16x16 through P_8x8ref0 macroblock types are supported.");
        if (numRefIdxL0ActiveMinus1 > 31)
            throw new ArgumentOutOfRangeException(nameof(numRefIdxL0ActiveMinus1));

        return macroblockHeader.MbTypeCode switch
        {
            0 => ReadRectangular(reader, 0, 16, 16, numRefIdxL0ActiveMinus1),
            1 => ReadRectangular(reader, 1, 16, 8, numRefIdxL0ActiveMinus1),
            2 => ReadRectangular(reader, 2, 8, 16, numRefIdxL0ActiveMinus1),
            3 => ReadSubMacroblocks(reader, 3, false, numRefIdxL0ActiveMinus1),
            4 => ReadSubMacroblocks(reader, 4, true, numRefIdxL0ActiveMinus1),
            _ => throw new InvalidOperationException()
        };
    }

    private static H264InterMacroblockSyntax ReadRectangular(
        H264BitReader reader, uint mbType, int partitionWidth, int partitionHeight,
        uint numRefIdxL0ActiveMinus1)
    {
        int count = partitionWidth == 16 && partitionHeight == 16 ? 1 : 2;
        H264InterPredictionPartition[] partitions = new H264InterPredictionPartition[count];
        uint[] referenceIndices = new uint[count];

        for (int i = 0; i < count; i++)
            referenceIndices[i] = ReadReferenceIndex(reader, numRefIdxL0ActiveMinus1, false);

        for (int i = 0; i < count; i++)
        {
            int x = partitionWidth == 8 ? (i == 0 ? 0 : 8) : 0;
            int y = partitionHeight == 8 ? (i == 0 ? 0 : 8) : 0;
            H264MotionVectorDifference mvd = ReadMvd(reader);
            H264InterSubPartition sub = new(x, y, partitionWidth, partitionHeight, [mvd]);
            partitions[i] = new H264InterPredictionPartition(
                x, y, partitionWidth, partitionHeight, referenceIndices[i], [sub]);
        }

        return new H264InterMacroblockSyntax(mbType, false, false, partitions);
    }

    private static H264InterMacroblockSyntax ReadSubMacroblocks(
        H264BitReader reader, uint mbType, bool forceReferenceZero,
        uint numRefIdxL0ActiveMinus1)
    {
        uint[] subTypes = new uint[4];
        for (int i = 0; i < 4; i++)
        {
            subTypes[i] = reader.ReadUnsignedExpGolomb();
            if (subTypes[i] > 3)
                throw new FormatException("P sub_mb_type must be in the range 0..3.");
        }

        uint[] referenceIndices = new uint[4];
        for (int i = 0; i < 4; i++)
            referenceIndices[i] = ReadReferenceIndex(reader, numRefIdxL0ActiveMinus1, forceReferenceZero);

        H264InterPredictionPartition[] partitions = new H264InterPredictionPartition[4];
        for (int i = 0; i < 4; i++)
        {
            int originX = (i & 1) * 8;
            int originY = (i >> 1) * 8;
            IReadOnlyList<H264InterSubPartition> subPartitions =
                ReadSubPartitions(reader, subTypes[i], originX, originY);
            partitions[i] = new H264InterPredictionPartition(
                originX, originY, 8, 8, referenceIndices[i], subPartitions);
        }

        return new H264InterMacroblockSyntax(mbType, true, forceReferenceZero, partitions);
    }

    private static IReadOnlyList<H264InterSubPartition> ReadSubPartitions(
        H264BitReader reader, uint subType, int originX, int originY)
    {
        (int width, int height, int count, bool horizontalSplit) = subType switch
        {
            0 => (8, 8, 1, false),
            1 => (8, 4, 2, true),
            2 => (4, 8, 2, false),
            3 => (4, 4, 4, false),
            _ => throw new FormatException("Unsupported P sub_mb_type.")
        };

        H264InterSubPartition[] result = new H264InterSubPartition[count];
        for (int i = 0; i < count; i++)
        {
            int x = originX + (horizontalSplit ? 0 : i * width);
            int y = originY + (horizontalSplit ? i * height : 0);
            result[i] = new H264InterSubPartition(x, y, width, height, [ReadMvd(reader)]);
        }
        return result;
    }

    private static uint ReadReferenceIndex(
        H264BitReader reader, uint numRefIdxL0ActiveMinus1, bool forceZero)
    {
        if (forceZero || numRefIdxL0ActiveMinus1 == 0)
            return 0;

        uint index = reader.ReadUnsignedExpGolomb();
        if (index > numRefIdxL0ActiveMinus1)
            throw new FormatException("ref_idx_l0 exceeds the active reference-list range.");
        return index;
    }

    private static H264MotionVectorDifference ReadMvd(H264BitReader reader) =>
        new(reader.ReadSignedExpGolomb(), reader.ReadSignedExpGolomb());
}
