using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parses frame-coded B-slice inter prediction syntax (mb_type 0..22).
/// Motion-vector prediction and pixel reconstruction are separate stages.
/// </summary>
public sealed class H264BMacroblockSyntaxReader
{
    private sealed record Shape(int X, int Y, int Width, int Height, H264BPredictionDirection Direction);

    public H264BMacroblockSyntax Read(
        H264BitReader reader,
        H264MacroblockHeader header,
        uint numRefIdxL0ActiveMinus1,
        uint numRefIdxL1ActiveMinus1)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(header);
        if (header.IsSkipped || header.IsIntra || header.IsPcm)
            throw new ArgumentException("A coded inter B macroblock header is required.", nameof(header));
        if (header.MbTypeCode > 22)
            throw new NotSupportedException("Only B-slice inter macroblock types 0..22 are supported.");
        if (numRefIdxL0ActiveMinus1 > 31 || numRefIdxL1ActiveMinus1 > 31)
            throw new ArgumentOutOfRangeException(nameof(numRefIdxL0ActiveMinus1));

        if (header.MbTypeCode == 0)
            return new H264BMacroblockSyntax(0, false,
                [new H264BPredictionPartition(0, 0, 16, 16, H264BPredictionDirection.Direct, null, null,
                    [new H264BSubPartition(0, 0, 16, 16, H264BPredictionDirection.Direct, null, null)])]);

        if (header.MbTypeCode == 22)
            return ReadEightByEight(reader, numRefIdxL0ActiveMinus1, numRefIdxL1ActiveMinus1);

        Shape[] shapes = GetMacroblockShapes(header.MbTypeCode);
        uint?[] refs0 = new uint?[shapes.Length];
        uint?[] refs1 = new uint?[shapes.Length];

        // ref_idx syntax is grouped by list, not interleaved by partition.
        for (int i = 0; i < shapes.Length; i++)
            if ((shapes[i].Direction & H264BPredictionDirection.List0) != 0)
                refs0[i] = ReadReferenceIndex(reader, numRefIdxL0ActiveMinus1);
        for (int i = 0; i < shapes.Length; i++)
            if ((shapes[i].Direction & H264BPredictionDirection.List1) != 0)
                refs1[i] = ReadReferenceIndex(reader, numRefIdxL1ActiveMinus1);

        H264MotionVectorDifference?[] mvd0 = new H264MotionVectorDifference?[shapes.Length];
        H264MotionVectorDifference?[] mvd1 = new H264MotionVectorDifference?[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
            if ((shapes[i].Direction & H264BPredictionDirection.List0) != 0)
                mvd0[i] = ReadMvd(reader);
        for (int i = 0; i < shapes.Length; i++)
            if ((shapes[i].Direction & H264BPredictionDirection.List1) != 0)
                mvd1[i] = ReadMvd(reader);

        H264BPredictionPartition[] partitions = new H264BPredictionPartition[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
        {
            Shape s = shapes[i];
            H264BSubPartition sub = new(s.X, s.Y, s.Width, s.Height,
                s.Direction, mvd0[i], mvd1[i]);
            partitions[i] = new H264BPredictionPartition(s.X, s.Y, s.Width, s.Height,
                s.Direction, refs0[i], refs1[i], [sub]);
        }
        return new H264BMacroblockSyntax(header.MbTypeCode, false, partitions);
    }

    private static H264BMacroblockSyntax ReadEightByEight(
        H264BitReader reader, uint refs0Minus1, uint refs1Minus1)
    {
        uint[] types = new uint[4];
        H264BPredictionDirection[] directions = new H264BPredictionDirection[4];
        for (int i = 0; i < 4; i++)
        {
            types[i] = reader.ReadUnsignedExpGolomb();
            if (types[i] > 12)
                throw new FormatException("B sub_mb_type must be in the range 0..12.");
            directions[i] = types[i] switch
            {
                0 => H264BPredictionDirection.Direct,
                1 or 4 or 5 or 10 => H264BPredictionDirection.List0,
                2 or 6 or 7 or 11 => H264BPredictionDirection.List1,
                _ => H264BPredictionDirection.Bi
            };
        }

        uint?[] ref0 = new uint?[4];
        uint?[] ref1 = new uint?[4];
        for (int i = 0; i < 4; i++)
            if ((directions[i] & H264BPredictionDirection.List0) != 0)
                ref0[i] = ReadReferenceIndex(reader, refs0Minus1);
        for (int i = 0; i < 4; i++)
            if ((directions[i] & H264BPredictionDirection.List1) != 0)
                ref1[i] = ReadReferenceIndex(reader, refs1Minus1);

        List<H264BSubPartition>[] subs = new List<H264BSubPartition>[4];
        for (int i = 0; i < 4; i++) subs[i] = BuildSubPartitions(i, types[i], directions[i]);
        for (int i = 0; i < 4; i++)
            if ((directions[i] & H264BPredictionDirection.List0) != 0)
                ReadMvdForSubs(reader, subs[i], true);
        for (int i = 0; i < 4; i++)
            if ((directions[i] & H264BPredictionDirection.List1) != 0)
                ReadMvdForSubs(reader, subs[i], false);

        H264BPredictionPartition[] partitions = new H264BPredictionPartition[4];
        for (int i = 0; i < 4; i++)
        {
            int x = (i & 1) * 8, y = (i >> 1) * 8;
            partitions[i] = new H264BPredictionPartition(x, y, 8, 8, directions[i],
                ref0[i], ref1[i], subs[i]);
        }
        return new H264BMacroblockSyntax(22, true, partitions);
    }

    private static List<H264BSubPartition> BuildSubPartitions(
        int index, uint type, H264BPredictionDirection direction)
    {
        int ox = (index & 1) * 8, oy = (index >> 1) * 8;
        (int w, int h, int count) = type switch
        {
            0 or 1 or 2 or 3 => (8, 8, 1),
            4 or 6 or 8 => (8, 4, 2),
            5 or 7 or 9 => (4, 8, 2),
            10 or 11 or 12 => (4, 4, 4),
            _ => throw new FormatException("Invalid B sub_mb_type.")
        };
        List<H264BSubPartition> result = new(count);
        for (int n = 0; n < count; n++)
        {
            int x = ox + (n % (8 / w)) * w;
            int y = oy + (n / (8 / w)) * h;
            result.Add(new H264BSubPartition(x, y, w, h, direction, null, null));
        }
        return result;
    }

    private static void ReadMvdForSubs(
        H264BitReader reader, List<H264BSubPartition> subs, bool list0)
    {
        for (int i = 0; i < subs.Count; i++)
        {
            H264MotionVectorDifference mvd = ReadMvd(reader);
            H264BSubPartition s = subs[i];
            subs[i] = list0 ? s with { MotionVectorDifferenceL0 = mvd }
                : s with { MotionVectorDifferenceL1 = mvd };
        }
    }

    private static Shape[] GetMacroblockShapes(uint type)
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
            return [new Shape(0, 0, 16, 16, directions[0])];
        return horizontal
            ? [new Shape(0, 0, 16, 8, directions[0]), new Shape(0, 8, 16, 8, directions[1])]
            : [new Shape(0, 0, 8, 16, directions[0]), new Shape(8, 0, 8, 16, directions[1])];
    }

    private static uint ReadReferenceIndex(H264BitReader reader, uint activeMinus1)
    {
        if (activeMinus1 == 0) return 0;
        uint value = reader.ReadUnsignedExpGolomb();
        if (value > activeMinus1)
            throw new FormatException("A B-slice reference index exceeds its active list.");
        return value;
    }

    private static H264MotionVectorDifference ReadMvd(H264BitReader reader) =>
        new(reader.ReadSignedExpGolomb(), reader.ReadSignedExpGolomb());
}
