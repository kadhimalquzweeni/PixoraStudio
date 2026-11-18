using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Resolves effective H.264 scaling matrices in raster order.</summary>
public static class H264ScalingMatrixResolver
{
    private static readonly int[] Flat4x4 = Enumerable.Repeat(16, 16).ToArray();
    private static readonly int[] Flat8x8 = Enumerable.Repeat(16, 64).ToArray();

    private static readonly int[] DefaultIntra4x4 =
    [
        6, 13, 20, 28, 13, 20, 28, 32,
        20, 28, 32, 37, 28, 32, 37, 42
    ];

    private static readonly int[] DefaultInter4x4 =
    [
        10, 14, 20, 24, 14, 20, 24, 27,
        20, 24, 27, 30, 24, 27, 30, 34
    ];

    private static readonly int[] DefaultIntra8x8 =
    [
        6, 10, 13, 16, 18, 23, 25, 27,
        10, 11, 16, 18, 23, 25, 27, 29,
        13, 16, 18, 23, 25, 27, 29, 31,
        16, 18, 23, 25, 27, 29, 31, 33,
        18, 23, 25, 27, 29, 31, 33, 36,
        23, 25, 27, 29, 31, 33, 36, 38,
        25, 27, 29, 31, 33, 36, 38, 40,
        27, 29, 31, 33, 36, 38, 40, 42
    ];

    private static readonly int[] DefaultInter8x8 =
    [
        9, 13, 15, 17, 19, 21, 22, 24,
        13, 13, 17, 19, 21, 22, 24, 25,
        15, 17, 19, 21, 22, 24, 25, 27,
        17, 19, 21, 22, 24, 25, 27, 28,
        19, 21, 22, 24, 25, 27, 28, 30,
        21, 22, 24, 25, 27, 28, 30, 32,
        22, 24, 25, 27, 28, 30, 32, 33,
        24, 25, 27, 28, 30, 32, 33, 35
    ];

    /// <summary>Returns six 4x4 matrices: IntraY/Cb/Cr followed by InterY/Cb/Cr.</summary>
    public static int[][] Resolve4x4(H264SequenceParameterSet sps, H264PictureParameterSetHeader pps)
    {
        ValidateParameterSets(sps, pps);
        int[][] sequenceLists = ResolveSequence4x4(sps);
        if (!pps.PicScalingMatrixPresentFlag)
            return CloneMatrices(sequenceLists);

        int[][] result = new int[6][];
        for (int i = 0; i < 6; i++)
        {
            if (IsListPresent(pps.ScalingLists, i, 16))
                result[i] = IsTrue(pps.ScalingListUseDefault, i) ? Default4x4(i) : H264CoefficientScanner.ToRasterOrder(pps.ScalingLists[i]);
            else if (sps.ScalingMatrixPresentFlag && i is 0 or 3)
                result[i] = (int[])sequenceLists[i].Clone();
            else if (!sps.ScalingMatrixPresentFlag && i is 0 or 3)
                result[i] = Default4x4(i);
            else
                result[i] = (int[])result[i - 1].Clone();
        }
        return result;
    }

    /// <summary>
    /// Returns the effective 8x8 matrices in syntax-list order. There are two matrices
    /// for 4:2:0/4:2:2 and six for 4:4:4; even indices are intra and odd indices inter.
    /// </summary>
    public static int[][] Resolve8x8(H264SequenceParameterSet sps, H264PictureParameterSetHeader pps)
    {
        ValidateParameterSets(sps, pps);
        if (!pps.Transform8X8ModeFlag)
            throw new FormatException("8x8 scaling matrices are not applicable when transform_8x8_mode_flag is false.");

        int count = sps.ChromaFormatIdc == 3 && !sps.SeparateColourPlaneFlag ? 6 : 2;
        int[][] sequenceLists = ResolveSequence8x8(sps, count);
        if (!pps.PicScalingMatrixPresentFlag)
            return CloneMatrices(sequenceLists);

        int[][] result = new int[count][];
        for (int i = 0; i < count; i++)
        {
            int listIndex = i + 6;
            if (IsListPresent(pps.ScalingLists, listIndex, 64))
            {
                result[i] = IsTrue(pps.ScalingListUseDefault, listIndex)
                    ? Default8x8(listIndex)
                    : H264CoefficientScanner.ToRasterOrder(pps.ScalingLists[listIndex]);
            }
            else if (sps.ScalingMatrixPresentFlag)
            {
                result[i] = (int[])sequenceLists[i].Clone();
            }
            else
            {
                result[i] = Default8x8(listIndex);
            }
        }

        return result;
    }

    private static int[][] ResolveSequence4x4(H264SequenceParameterSet sps)
    {
        int[][] result = new int[6][];
        for (int i = 0; i < 6; i++)
        {
            if (!sps.ScalingMatrixPresentFlag)
                result[i] = (int[])Flat4x4.Clone();
            else if (IsListPresent(sps.ScalingLists, i, 16))
                result[i] = IsTrue(sps.ScalingListUseDefault, i) ? Default4x4(i) : H264CoefficientScanner.ToRasterOrder(sps.ScalingLists[i]);
            else if (i is 0 or 3)
                result[i] = Default4x4(i);
            else
                result[i] = (int[])result[i - 1].Clone();
        }
        return result;
    }

    private static int[][] ResolveSequence8x8(H264SequenceParameterSet sps, int count)
    {
        int[][] result = new int[count][];
        for (int i = 0; i < count; i++)
        {
            int listIndex = i + 6;
            if (!sps.ScalingMatrixPresentFlag)
                result[i] = (int[])Flat8x8.Clone();
            else if (IsListPresent(sps.ScalingLists, listIndex, 64))
                result[i] = IsTrue(sps.ScalingListUseDefault, listIndex) ? Default8x8(listIndex) : H264CoefficientScanner.ToRasterOrder(sps.ScalingLists[listIndex]);
            else
                result[i] = Default8x8(listIndex);
        }
        return result;
    }

    private static int[] Default4x4(int index) =>
        (int[])(index < 3 ? DefaultIntra4x4 : DefaultInter4x4).Clone();

    private static int[] Default8x8(int index) =>
        (int[])((index & 1) == 0 ? DefaultIntra8x8 : DefaultInter8x8).Clone();

    private static bool IsListPresent(IReadOnlyList<IReadOnlyList<int>> lists, int index, int size) =>
        index < lists.Count && lists[index].Count == size;

    private static bool IsTrue(IReadOnlyList<bool> flags, int index) =>
        index < flags.Count && flags[index];

    private static int[][] CloneMatrices(int[][] matrices) =>
        matrices.Select(matrix => (int[])matrix.Clone()).ToArray();

    private static void ValidateParameterSets(H264SequenceParameterSet sps, H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        if (pps.SequenceParameterSetId != sps.Id)
            throw new FormatException("The PPS references a different SPS.");
    }
}
