using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Resolves the effective H.264 4x4 scaling matrices from SPS/PPS syntax.
/// Returned matrices contain six 16-entry arrays in raster order.
/// </summary>
public static class H264ScalingMatrixResolver
{
    private static readonly int[] Flat = Enumerable.Repeat(16, 16).ToArray();

    private static readonly int[] DefaultIntra =
    [
        6, 13, 20, 28,
        13, 20, 28, 32,
        20, 28, 32, 37,
        28, 32, 37, 42
    ];

    private static readonly int[] DefaultInter =
    [
        10, 14, 20, 24,
        14, 20, 24, 27,
        20, 24, 27, 30,
        24, 27, 30, 34
    ];

    /// <summary>
    /// Resolves the six effective 4x4 matrices used by luma and 4:2:0/4:2:2 chroma.
    /// The result is indexed as IntraY, IntraCb, IntraCr, InterY, InterCb, InterCr.
    /// </summary>
    public static int[][] Resolve4x4(
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (pps.SequenceParameterSetId != sps.Id)
            throw new FormatException("The PPS references a different SPS.");

        int[][] sequenceLists = ResolveSequenceLists(sps);
        if (!pps.PicScalingMatrixPresentFlag)
            return CloneMatrices(sequenceLists);

        int[][] result = new int[6][];
        for (int i = 0; i < 6; i++)
        {
            if (IsListPresent(pps.ScalingLists, i))
            {
                bool useDefault = IsTrue(pps.ScalingListUseDefault, i);
                result[i] = useDefault
                    ? GetDefault(i)
                    : H264CoefficientScanner.ToRasterOrder(pps.ScalingLists[i]);
                continue;
            }

            // Table 7-2 fallback rule B uses the sequence-level luma
            // intra/inter matrices as anchors, then chains the chroma lists.
            if (sps.ScalingMatrixPresentFlag && i == 0)
            {
                result[i] = (int[])sequenceLists[0].Clone();
            }
            else if (sps.ScalingMatrixPresentFlag && i == 3)
            {
                result[i] = (int[])sequenceLists[3].Clone();
            }
            else if (!sps.ScalingMatrixPresentFlag && i == 0)
            {
                result[i] = (int[])DefaultIntra.Clone();
            }
            else if (!sps.ScalingMatrixPresentFlag && i == 3)
            {
                result[i] = (int[])DefaultInter.Clone();
            }
            else
            {
                result[i] = (int[])result[i - 1].Clone();
            }
        }

        return result;
    }

    private static int[][] ResolveSequenceLists(H264SequenceParameterSet sps)
    {
        int[][] result = new int[6][];
        if (!sps.ScalingMatrixPresentFlag)
        {
            for (int i = 0; i < 6; i++)
                result[i] = (int[])Flat.Clone();
            return result;
        }

        for (int i = 0; i < 6; i++)
        {
            if (IsListPresent(sps.ScalingLists, i))
            {
                result[i] = IsTrue(sps.ScalingListUseDefault, i)
                    ? GetDefault(i)
                    : H264CoefficientScanner.ToRasterOrder(sps.ScalingLists[i]);
            }
            else if (i == 0)
            {
                result[i] = (int[])DefaultIntra.Clone();
            }
            else if (i == 3)
            {
                result[i] = (int[])DefaultInter.Clone();
            }
            else
            {
                result[i] = (int[])result[i - 1].Clone();
            }
        }

        return result;
    }

    private static int[] GetDefault(int index) =>
        (int[])(index < 3 ? DefaultIntra : DefaultInter).Clone();

    private static bool IsListPresent(IReadOnlyList<IReadOnlyList<int>> lists, int index) =>
        index < lists.Count && lists[index].Count == 16;

    private static bool IsTrue(IReadOnlyList<bool> flags, int index) =>
        index < flags.Count && flags[index];

    private static int[][] CloneMatrices(int[][] matrices) =>
        matrices.Select(matrix => (int[])matrix.Clone()).ToArray();
}
