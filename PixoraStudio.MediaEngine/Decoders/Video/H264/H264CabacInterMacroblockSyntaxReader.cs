using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parses CABAC P/B inter-prediction syntax and updates the per-4x4 motion
/// neighbour state needed by subsequent reference-index and MVD context bins.
/// Prediction and reconstruction remain separate stages.
/// </summary>
public sealed class H264CabacInterMacroblockSyntaxReader
{
    private sealed record PartitionShape(
        int X, int Y, int Width, int Height, H264BPredictionDirection Direction);

    public H264InterMacroblockSyntax ReadPSlice(
        H264CabacSyntaxElementReader reader,
        H264MacroblockHeader header,
        uint numRefIdxL0ActiveMinus1,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264CabacMotionVectorNeighbourContext neighbourContext)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(neighbourContext);
        ValidateMacroblock(header, macroblockX, macroblockY, sliceId);
        if (header.IsSkipped || header.IsIntra || header.MbTypeCode > 4)
            throw new ArgumentException("A coded inter P-slice macroblock header is required.", nameof(header));
        if (numRefIdxL0ActiveMinus1 > 31)
            throw new ArgumentOutOfRangeException(nameof(numRefIdxL0ActiveMinus1));

        return header.MbTypeCode switch
        {
            0 => ReadPRectangular(reader, 0, 16, 16, numRefIdxL0ActiveMinus1,
                macroblockX, macroblockY, sliceId, neighbourContext),
            1 => ReadPRectangular(reader, 1, 16, 8, numRefIdxL0ActiveMinus1,
                macroblockX, macroblockY, sliceId, neighbourContext),
            2 => ReadPRectangular(reader, 2, 8, 16, numRefIdxL0ActiveMinus1,
                macroblockX, macroblockY, sliceId, neighbourContext),
            3 => ReadPSubMacroblocks(reader, 3, false, numRefIdxL0ActiveMinus1,
                macroblockX, macroblockY, sliceId, neighbourContext),
            4 => ReadPSubMacroblocks(reader, 4, true, numRefIdxL0ActiveMinus1,
                macroblockX, macroblockY, sliceId, neighbourContext),
            _ => throw new InvalidOperationException()
        };
    }

    public H264BMacroblockSyntax ReadBSlice(
        H264CabacSyntaxElementReader reader,
        H264MacroblockHeader header,
        uint numRefIdxL0ActiveMinus1,
        uint numRefIdxL1ActiveMinus1,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264CabacMotionVectorNeighbourContext neighbourContext)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(neighbourContext);
        ValidateMacroblock(header, macroblockX, macroblockY, sliceId);
        if (header.IsSkipped || header.IsIntra || header.MbTypeCode > 22)
            throw new ArgumentException("A coded inter B-slice macroblock header is required.", nameof(header));
        if (numRefIdxL0ActiveMinus1 > 31 || numRefIdxL1ActiveMinus1 > 31)
            throw new ArgumentOutOfRangeException(nameof(numRefIdxL0ActiveMinus1));

        if (header.MbTypeCode == 0)
            return ReadBDirectMacroblock(macroblockX, macroblockY, sliceId, neighbourContext);
        if (header.MbTypeCode == 22)
            return ReadBSubMacroblocks(reader, numRefIdxL0ActiveMinus1,
                numRefIdxL1ActiveMinus1, macroblockX, macroblockY, sliceId, neighbourContext);

        PartitionShape[] shapes = GetBMacroblockShapes(header.MbTypeCode);
        uint?[] referencesL0 = new uint?[shapes.Length];
        uint?[] referencesL1 = new uint?[shapes.Length];

        for (int i = 0; i < shapes.Length; i++)
        {
            if ((shapes[i].Direction & H264BPredictionDirection.List0) == 0)
                continue;

            referencesL0[i] = ReadReferenceIndex(
                reader, neighbourContext, 0, shapes[i], macroblockX, macroblockY,
                sliceId, numRefIdxL0ActiveMinus1, isBSlice: true);
            RecordPartition(neighbourContext, 0, shapes[i], macroblockX, macroblockY,
                sliceId, checked((int)referencesL0[i]!.Value), isDirect: false, 0, 0);
        }
        for (int i = 0; i < shapes.Length; i++)
        {
            if ((shapes[i].Direction & H264BPredictionDirection.List1) == 0)
                continue;

            referencesL1[i] = ReadReferenceIndex(
                reader, neighbourContext, 1, shapes[i], macroblockX, macroblockY,
                sliceId, numRefIdxL1ActiveMinus1, isBSlice: true);
            RecordPartition(neighbourContext, 1, shapes[i], macroblockX, macroblockY,
                sliceId, checked((int)referencesL1[i]!.Value), isDirect: false, 0, 0);
        }

        H264MotionVectorDifference?[] mvdL0 = new H264MotionVectorDifference?[shapes.Length];
        H264MotionVectorDifference?[] mvdL1 = new H264MotionVectorDifference?[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
        {
            if ((shapes[i].Direction & H264BPredictionDirection.List0) == 0)
            {
                if ((shapes[i].Direction & H264BPredictionDirection.List1) == 0)
                    RecordPartition(neighbourContext, 0, shapes[i], macroblockX, macroblockY,
                        sliceId, -1, isDirect: true, 0, 0);
                else
                    RecordPartition(neighbourContext, 0, shapes[i], macroblockX, macroblockY,
                        sliceId, -1, isDirect: false, 0, 0);
                continue;
            }

            mvdL0[i] = ReadMvd(reader, neighbourContext, 0, shapes[i],
                macroblockX, macroblockY, sliceId);
            RecordPartition(neighbourContext, 0, shapes[i], macroblockX, macroblockY,
                sliceId, checked((int)referencesL0[i]!.Value), isDirect: false,
                mvdL0[i]!.Horizontal, mvdL0[i]!.Vertical);
        }
        for (int i = 0; i < shapes.Length; i++)
        {
            if ((shapes[i].Direction & H264BPredictionDirection.List1) == 0)
            {
                if ((shapes[i].Direction & H264BPredictionDirection.List0) == 0)
                    RecordPartition(neighbourContext, 1, shapes[i], macroblockX, macroblockY,
                        sliceId, -1, isDirect: true, 0, 0);
                else
                    RecordPartition(neighbourContext, 1, shapes[i], macroblockX, macroblockY,
                        sliceId, -1, isDirect: false, 0, 0);
                continue;
            }

            mvdL1[i] = ReadMvd(reader, neighbourContext, 1, shapes[i],
                macroblockX, macroblockY, sliceId);
            RecordPartition(neighbourContext, 1, shapes[i], macroblockX, macroblockY,
                sliceId, checked((int)referencesL1[i]!.Value), isDirect: false,
                mvdL1[i]!.Horizontal, mvdL1[i]!.Vertical);
        }

        H264BPredictionPartition[] partitions = new H264BPredictionPartition[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
        {
            PartitionShape shape = shapes[i];
            H264BSubPartition sub = new(shape.X, shape.Y, shape.Width, shape.Height,
                shape.Direction, mvdL0[i], mvdL1[i]);
            partitions[i] = new H264BPredictionPartition(
                shape.X, shape.Y, shape.Width, shape.Height, shape.Direction,
                referencesL0[i], referencesL1[i], [sub]);
        }

        return new H264BMacroblockSyntax(header.MbTypeCode, false, partitions);
    }

    public static bool HasNoSubMacroblockPartitionSmallerThan8x8(
        H264InterMacroblockSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        return syntax.Partitions.SelectMany(partition => partition.SubPartitions)
            .All(sub => sub.Width >= 8 && sub.Height >= 8);
    }

    public static bool HasNoSubMacroblockPartitionSmallerThan8x8(
        H264BMacroblockSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        return syntax.Partitions.SelectMany(partition => partition.SubPartitions)
            .All(sub => sub.Width >= 8 && sub.Height >= 8);
    }

    public static bool IsDirectMacroblock(H264BMacroblockSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        return syntax.Partitions.SelectMany(partition => partition.SubPartitions)
            .All(sub => sub.Direction == H264BPredictionDirection.Direct);
    }

    private static H264InterMacroblockSyntax ReadPRectangular(
        H264CabacSyntaxElementReader reader,
        uint mbType,
        int partitionWidth,
        int partitionHeight,
        uint activeReferencesMinus1,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264CabacMotionVectorNeighbourContext context)
    {
        int count = partitionWidth == 16 && partitionHeight == 16 ? 1 : 2;
        PartitionShape[] shapes = new PartitionShape[count];
        for (int i = 0; i < count; i++)
        {
            int x = partitionWidth == 8 ? (i == 0 ? 0 : 8) : 0;
            int y = partitionHeight == 8 ? (i == 0 ? 0 : 8) : 0;
            shapes[i] = new PartitionShape(x, y, partitionWidth, partitionHeight,
                H264BPredictionDirection.List0);
        }

        uint[] references = new uint[count];
        for (int i = 0; i < count; i++)
        {
            references[i] = ReadReferenceIndex(reader, context, 0, shapes[i],
                macroblockX, macroblockY, sliceId, activeReferencesMinus1, isBSlice: false);
            RecordPartition(context, 0, shapes[i], macroblockX, macroblockY,
                sliceId, checked((int)references[i]), isDirect: false, 0, 0);
        }

        H264InterPredictionPartition[] partitions = new H264InterPredictionPartition[count];
        for (int i = 0; i < count; i++)
        {
            H264MotionVectorDifference mvd = ReadMvd(reader, context, 0, shapes[i],
                macroblockX, macroblockY, sliceId);
            RecordPartition(context, 0, shapes[i], macroblockX, macroblockY,
                sliceId, checked((int)references[i]), isDirect: false,
                mvd.Horizontal, mvd.Vertical);
            PartitionShape shape = shapes[i];
            H264InterSubPartition sub = new(shape.X, shape.Y, shape.Width, shape.Height, [mvd]);
            partitions[i] = new H264InterPredictionPartition(
                shape.X, shape.Y, shape.Width, shape.Height, references[i], [sub]);
        }

        return new H264InterMacroblockSyntax(mbType, false, false, partitions);
    }

    private static H264InterMacroblockSyntax ReadPSubMacroblocks(
        H264CabacSyntaxElementReader reader,
        uint mbType,
        bool forceReferenceZero,
        uint activeReferencesMinus1,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264CabacMotionVectorNeighbourContext context)
    {
        int[] subTypes = new int[4];
        for (int i = 0; i < 4; i++)
        {
            H264CabacDiagnosticTrace.Mark(
                reader.BinReader, $"mb({macroblockX},{macroblockY}) sub_mb_type[{i}] begin");
            subTypes[i] = reader.ReadPSubMacroblockType();
            H264CabacDiagnosticTrace.Mark(
                reader.BinReader, $"mb({macroblockX},{macroblockY}) sub_mb_type[{i}]={subTypes[i]}");
        }

        PartitionShape[][] subShapes = new PartitionShape[4][];
        PartitionShape[] parentShapes = new PartitionShape[4];
        for (int i = 0; i < 4; i++)
        {
            int originX = (i & 1) * 8;
            int originY = (i >> 1) * 8;
            parentShapes[i] = new PartitionShape(originX, originY, 8, 8,
                H264BPredictionDirection.List0);
            subShapes[i] = BuildPSubPartitions(subTypes[i], originX, originY);
        }

        uint[] references = new uint[4];
        for (int i = 0; i < 4; i++)
        {
            H264CabacDiagnosticTrace.Mark(
                reader.BinReader, $"mb({macroblockX},{macroblockY}) ref_idx_l0[{i}] begin");
            references[i] = forceReferenceZero
                ? 0
                : ReadReferenceIndex(reader, context, 0, parentShapes[i],
                    macroblockX, macroblockY, sliceId, activeReferencesMinus1, isBSlice: false);
            H264CabacDiagnosticTrace.Mark(
                reader.BinReader, $"mb({macroblockX},{macroblockY}) ref_idx_l0[{i}]={references[i]}");
            RecordPartition(context, 0, parentShapes[i], macroblockX, macroblockY,
                sliceId, checked((int)references[i]), isDirect: false, 0, 0);
        }

        H264InterPredictionPartition[] partitions = new H264InterPredictionPartition[4];
        for (int i = 0; i < 4; i++)
        {
            H264InterSubPartition[] subPartitions = new H264InterSubPartition[subShapes[i].Length];
            for (int j = 0; j < subShapes[i].Length; j++)
            {
                PartitionShape shape = subShapes[i][j];
                H264CabacDiagnosticTrace.Mark(
                    reader.BinReader,
                    $"mb({macroblockX},{macroblockY}) mvd_l0[{i},{j}] " +
                        $"region=({shape.X},{shape.Y},{shape.Width},{shape.Height}) begin");
                H264MotionVectorDifference mvd = ReadMvd(reader, context, 0, shape,
                    macroblockX, macroblockY, sliceId);
                H264CabacDiagnosticTrace.Mark(
                    reader.BinReader,
                    $"mb({macroblockX},{macroblockY}) mvd_l0[{i},{j}]=({mvd.Horizontal},{mvd.Vertical})");
                RecordPartition(context, 0, shape, macroblockX, macroblockY,
                    sliceId, checked((int)references[i]), isDirect: false,
                    mvd.Horizontal, mvd.Vertical);
                subPartitions[j] = new H264InterSubPartition(
                    shape.X, shape.Y, shape.Width, shape.Height, [mvd]);
            }

            PartitionShape parent = parentShapes[i];
            partitions[i] = new H264InterPredictionPartition(
                parent.X, parent.Y, 8, 8, references[i], subPartitions);
        }

        return new H264InterMacroblockSyntax(mbType, true, forceReferenceZero, partitions);
    }

    private static H264BMacroblockSyntax ReadBDirectMacroblock(
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264CabacMotionVectorNeighbourContext context)
    {
        PartitionShape shape = new(0, 0, 16, 16, H264BPredictionDirection.Direct);
        RecordPartition(context, 0, shape, macroblockX, macroblockY,
            sliceId, -1, isDirect: true, 0, 0);
        RecordPartition(context, 1, shape, macroblockX, macroblockY,
            sliceId, -1, isDirect: true, 0, 0);
        H264BSubPartition sub = new(0, 0, 16, 16,
            H264BPredictionDirection.Direct, null, null);
        H264BPredictionPartition partition = new(0, 0, 16, 16,
            H264BPredictionDirection.Direct, null, null, [sub]);
        return new H264BMacroblockSyntax(0, false, [partition]);
    }

    private static H264BMacroblockSyntax ReadBSubMacroblocks(
        H264CabacSyntaxElementReader reader,
        uint activeL0Minus1,
        uint activeL1Minus1,
        int macroblockX,
        int macroblockY,
        int sliceId,
        H264CabacMotionVectorNeighbourContext context)
    {
        int[] types = new int[4];
        H264BPredictionDirection[] directions = new H264BPredictionDirection[4];
        PartitionShape[][] subShapes = new PartitionShape[4][];
        PartitionShape[] parentShapes = new PartitionShape[4];

        for (int i = 0; i < 4; i++)
        {
            types[i] = reader.ReadBSubMacroblockType();
            directions[i] = GetBSubMacroblockDirection(types[i]);
            int originX = (i & 1) * 8;
            int originY = (i >> 1) * 8;
            parentShapes[i] = new PartitionShape(originX, originY, 8, 8, directions[i]);
            subShapes[i] = BuildBSubPartitions(i, types[i], directions[i]);
        }

        uint?[] refs0 = new uint?[4];
        uint?[] refs1 = new uint?[4];
        for (int i = 0; i < 4; i++)
        {
            if ((directions[i] & H264BPredictionDirection.List0) != 0)
                refs0[i] = ReadReferenceIndex(reader, context, 0, parentShapes[i],
                    macroblockX, macroblockY, sliceId, activeL0Minus1, isBSlice: true);
            RecordPartition(context, 0, parentShapes[i], macroblockX, macroblockY,
                sliceId, refs0[i] is uint r0 ? checked((int)r0) : -1,
                directions[i] == H264BPredictionDirection.Direct, 0, 0);
        }
        for (int i = 0; i < 4; i++)
        {
            if ((directions[i] & H264BPredictionDirection.List1) != 0)
                refs1[i] = ReadReferenceIndex(reader, context, 1, parentShapes[i],
                    macroblockX, macroblockY, sliceId, activeL1Minus1, isBSlice: true);
            RecordPartition(context, 1, parentShapes[i], macroblockX, macroblockY,
                sliceId, refs1[i] is uint r1 ? checked((int)r1) : -1,
                directions[i] == H264BPredictionDirection.Direct, 0, 0);
        }

        H264MotionVectorDifference?[][] mvd0 = new H264MotionVectorDifference?[4][];
        H264MotionVectorDifference?[][] mvd1 = new H264MotionVectorDifference?[4][];
        for (int i = 0; i < 4; i++)
        {
            mvd0[i] = new H264MotionVectorDifference?[subShapes[i].Length];
            if ((directions[i] & H264BPredictionDirection.List0) == 0)
                continue;
            for (int j = 0; j < subShapes[i].Length; j++)
            {
                PartitionShape shape = subShapes[i][j];
                mvd0[i][j] = ReadMvd(reader, context, 0, shape,
                    macroblockX, macroblockY, sliceId);
                RecordPartition(context, 0, shape, macroblockX, macroblockY,
                    sliceId, checked((int)refs0[i]!.Value), isDirect: false,
                    mvd0[i][j]!.Horizontal, mvd0[i][j]!.Vertical);
            }
        }
        for (int i = 0; i < 4; i++)
        {
            mvd1[i] = new H264MotionVectorDifference?[subShapes[i].Length];
            if ((directions[i] & H264BPredictionDirection.List1) == 0)
                continue;
            for (int j = 0; j < subShapes[i].Length; j++)
            {
                PartitionShape shape = subShapes[i][j];
                mvd1[i][j] = ReadMvd(reader, context, 1, shape,
                    macroblockX, macroblockY, sliceId);
                RecordPartition(context, 1, shape, macroblockX, macroblockY,
                    sliceId, checked((int)refs1[i]!.Value), isDirect: false,
                    mvd1[i][j]!.Horizontal, mvd1[i][j]!.Vertical);
            }
        }

        H264BPredictionPartition[] partitions = new H264BPredictionPartition[4];
        for (int i = 0; i < 4; i++)
        {
            H264BSubPartition[] subs = new H264BSubPartition[subShapes[i].Length];
            for (int j = 0; j < subShapes[i].Length; j++)
            {
                PartitionShape shape = subShapes[i][j];
                subs[j] = new H264BSubPartition(shape.X, shape.Y,
                    shape.Width, shape.Height, directions[i], mvd0[i][j], mvd1[i][j]);
            }
            PartitionShape parent = parentShapes[i];
            partitions[i] = new H264BPredictionPartition(
                parent.X, parent.Y, 8, 8, directions[i], refs0[i], refs1[i], subs);
        }

        return new H264BMacroblockSyntax(22, true, partitions);
    }

    private static PartitionShape[] GetBMacroblockShapes(uint type)
    {
        H264BPredictionDirection l0 = H264BPredictionDirection.List0;
        H264BPredictionDirection l1 = H264BPredictionDirection.List1;
        H264BPredictionDirection bi = H264BPredictionDirection.Bi;
        H264BPredictionDirection[] directions = type switch
        {
            1 => [l0], 2 => [l1], 3 => [bi],
            4 => [l0, l0], 5 => [l0, l0],
            6 => [l1, l1], 7 => [l1, l1],
            8 => [l0, l1], 9 => [l0, l1],
            10 => [l1, l0], 11 => [l1, l0],
            12 => [l0, bi], 13 => [l0, bi],
            14 => [l1, bi], 15 => [l1, bi],
            16 => [bi, l0], 17 => [bi, l0],
            18 => [bi, l1], 19 => [bi, l1],
            20 => [bi, bi], 21 => [bi, bi],
            _ => throw new NotSupportedException("Unsupported B-slice macroblock type.")
        };
        bool horizontal = type is 4 or 6 or 8 or 10 or 12 or 14 or 16 or 18 or 20;
        if (directions.Length == 1)
            return [new PartitionShape(0, 0, 16, 16, directions[0])];
        return horizontal
            ? [new PartitionShape(0, 0, 16, 8, directions[0]),
               new PartitionShape(0, 8, 16, 8, directions[1])]
            : [new PartitionShape(0, 0, 8, 16, directions[0]),
               new PartitionShape(8, 0, 8, 16, directions[1])];
    }

    private static PartitionShape[] BuildPSubPartitions(int type, int originX, int originY)
    {
        (int width, int height, int count) = type switch
        {
            0 => (8, 8, 1),
            1 => (8, 4, 2),
            2 => (4, 8, 2),
            3 => (4, 4, 4),
            _ => throw new FormatException("Invalid P sub_mb_type.")
        };
        return Enumerable.Range(0, count).Select(i =>
        {
            int perRow = 8 / width;
            return new PartitionShape(
                originX + (i % perRow) * width,
                originY + (i / perRow) * height,
                width, height, H264BPredictionDirection.List0);
        }).ToArray();
    }

    private static PartitionShape[] BuildBSubPartitions(
        int index, int type, H264BPredictionDirection direction)
    {
        int originX = (index & 1) * 8;
        int originY = (index >> 1) * 8;
        (int width, int height, int count) = type switch
        {
            0 or 1 or 2 or 3 => (8, 8, 1),
            4 or 6 or 8 => (8, 4, 2),
            5 or 7 or 9 => (4, 8, 2),
            10 or 11 or 12 => (4, 4, 4),
            _ => throw new FormatException("Invalid B sub_mb_type.")
        };
        return Enumerable.Range(0, count).Select(i =>
        {
            int perRow = 8 / width;
            return new PartitionShape(
                originX + (i % perRow) * width,
                originY + (i / perRow) * height,
                width, height, direction);
        }).ToArray();
    }

    private static H264BPredictionDirection GetBSubMacroblockDirection(int type) =>
        type switch
        {
            0 => H264BPredictionDirection.Direct,
            1 or 4 or 5 or 10 => H264BPredictionDirection.List0,
            2 or 6 or 7 or 11 => H264BPredictionDirection.List1,
            _ => H264BPredictionDirection.Bi
        };

    private static uint ReadReferenceIndex(
        H264CabacSyntaxElementReader reader,
        H264CabacMotionVectorNeighbourContext context,
        int list,
        PartitionShape shape,
        int macroblockX,
        int macroblockY,
        int sliceId,
        uint activeReferencesMinus1,
        bool isBSlice)
    {
        if (activeReferencesMinus1 == 0)
            return 0;

        (int blockX, int blockY) = GetBlockOrigin(shape, macroblockX, macroblockY);
        int increment = context.GetReferenceIndexContextIncrement(
            list, blockX, blockY, sliceId, isBSlice);
        return checked((uint)reader.ReadReferenceIndex(
            increment, checked((int)activeReferencesMinus1)));
    }

    private static H264MotionVectorDifference ReadMvd(
        H264CabacSyntaxElementReader reader,
        H264CabacMotionVectorNeighbourContext context,
        int list,
        PartitionShape shape,
        int macroblockX,
        int macroblockY,
        int sliceId)
    {
        (int blockX, int blockY) = GetBlockOrigin(shape, macroblockX, macroblockY);
        string location =
            $"mvd list={list} block=({blockX},{blockY}) " +
            $"region=({shape.X},{shape.Y},{shape.Width},{shape.Height})";
        H264CabacDiagnosticTrace.Mark(reader.BinReader, $"{location} begin");

        int horizontalIncrement = context.GetMotionVectorDifferenceContextIncrement(
            list, blockX, blockY, component: 0, sliceId);
        H264CabacDiagnosticTrace.Mark(
            reader.BinReader,
            $"{location} component=x context={40 + horizontalIncrement} " +
                $"increment={horizontalIncrement} begin");
        int horizontal = reader.ReadMotionVectorDifference(40, horizontalIncrement);
        H264CabacDiagnosticTrace.Mark(
            reader.BinReader, $"{location} component=x value={horizontal}");

        int verticalIncrement = context.GetMotionVectorDifferenceContextIncrement(
            list, blockX, blockY, component: 1, sliceId);
        H264CabacDiagnosticTrace.Mark(
            reader.BinReader,
            $"{location} component=y context={47 + verticalIncrement} " +
                $"increment={verticalIncrement} begin");
        int vertical = reader.ReadMotionVectorDifference(47, verticalIncrement);
        H264CabacDiagnosticTrace.Mark(
            reader.BinReader, $"{location} component=y value={vertical}");

        H264MotionVectorDifference result = new(horizontal, vertical);
        H264CabacDiagnosticTrace.Mark(
            reader.BinReader, $"{location} complete value=({horizontal},{vertical})");
        return result;
    }

    private static void RecordPartition(
        H264CabacMotionVectorNeighbourContext context,
        int list,
        PartitionShape shape,
        int macroblockX,
        int macroblockY,
        int sliceId,
        int referenceIndex,
        bool isDirect,
        int mvdX,
        int mvdY)
    {
        int originX = checked(macroblockX * 4 + shape.X / 4);
        int originY = checked(macroblockY * 4 + shape.Y / 4);
        int width = shape.Width / 4;
        int height = shape.Height / 4;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            context.Record(list, originX + x, originY + y, sliceId,
                referenceIndex, isDirect, mvdX, mvdY);
    }

    private static (int X, int Y) GetBlockOrigin(
        PartitionShape shape, int macroblockX, int macroblockY) =>
        (checked(macroblockX * 4 + shape.X / 4),
         checked(macroblockY * 4 + shape.Y / 4));

    private static void ValidateMacroblock(
        H264MacroblockHeader header, int macroblockX, int macroblockY, int sliceId)
    {
        if (macroblockX < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if (macroblockY < 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
    }
}
