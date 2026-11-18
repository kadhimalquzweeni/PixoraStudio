using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264ScalingMatrixResolverTests
{
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

    [Fact]
    public void Resolve4x4_ShouldUseFlatMatricesWhenSpsScalingMatrixIsAbsent()
    {
        int[][] result = H264ScalingMatrixResolver.Resolve4x4(CreateSps(), CreatePps());

        Assert.Equal(6, result.Length);
        Assert.All(result, matrix => Assert.Equal(Enumerable.Repeat(16, 16), matrix));
    }

    [Fact]
    public void Resolve4x4_ShouldApplySpsDefaultAndPreviousListFallbacks()
    {
        H264SequenceParameterSet sps = CreateSps() with
        {
            ScalingMatrixPresentFlag = true,
            ScalingLists = EmptyLists(8),
            ScalingListUseDefault = new bool[8]
        };

        int[][] result = H264ScalingMatrixResolver.Resolve4x4(sps, CreatePps());

        Assert.Equal(DefaultIntra, result[0]);
        Assert.Equal(DefaultIntra, result[1]);
        Assert.Equal(DefaultIntra, result[2]);
        Assert.Equal(DefaultInter, result[3]);
        Assert.Equal(DefaultInter, result[4]);
        Assert.Equal(DefaultInter, result[5]);
    }

    [Fact]
    public void Resolve4x4_ShouldConvertExplicitSpsListsFromScanToRasterOrder()
    {
        int[] scanOrder = Enumerable.Range(1, 16).ToArray();
        IReadOnlyList<int>[] lists = EmptyLists(8);
        lists[0] = scanOrder;

        H264SequenceParameterSet sps = CreateSps() with
        {
            ScalingMatrixPresentFlag = true,
            ScalingLists = lists,
            ScalingListUseDefault = new bool[8]
        };

        int[][] result = H264ScalingMatrixResolver.Resolve4x4(sps, CreatePps());

        Assert.Equal(H264CoefficientScanner.ToRasterOrder(scanOrder), result[0]);
        Assert.Equal(result[0], result[1]);
        Assert.Equal(result[0], result[2]);
    }

    [Fact]
    public void Resolve4x4_ShouldUseDefaultMatrixWhenScalingListSignalsUseDefault()
    {
        IReadOnlyList<int>[] lists = EmptyLists(8);
        lists[0] = Enumerable.Repeat(8, 16).ToArray();
        bool[] useDefault = new bool[8];
        useDefault[0] = true;

        H264SequenceParameterSet sps = CreateSps() with
        {
            ScalingMatrixPresentFlag = true,
            ScalingLists = lists,
            ScalingListUseDefault = useDefault
        };

        int[][] result = H264ScalingMatrixResolver.Resolve4x4(sps, CreatePps());

        Assert.Equal(DefaultIntra, result[0]);
    }

    [Fact]
    public void Resolve4x4_ShouldApplyPpsOverridesAndSpsFallbacks()
    {
        IReadOnlyList<int>[] spsLists = EmptyLists(8);
        int[] spsCb = Enumerable.Repeat(22, 16).ToArray();
        spsLists[1] = spsCb;
        H264SequenceParameterSet sps = CreateSps() with
        {
            ScalingMatrixPresentFlag = true,
            ScalingLists = spsLists,
            ScalingListUseDefault = new bool[8]
        };

        IReadOnlyList<int>[] ppsLists = EmptyLists(6);
        int[] ppsIntra = Enumerable.Repeat(31, 16).ToArray();
        ppsLists[0] = ppsIntra;
        H264PictureParameterSetHeader pps = CreatePps() with
        {
            PicScalingMatrixPresentFlag = true,
            ScalingLists = ppsLists,
            ScalingListUseDefault = new bool[6]
        };

        int[][] result = H264ScalingMatrixResolver.Resolve4x4(sps, pps);

        Assert.Equal(ppsIntra, H264CoefficientScanner.ToScanOrder(result[0]));
        Assert.Equal(22, Assert.Single(result[1].Distinct()));
        Assert.Equal(result[1], result[2]);
        Assert.Equal(DefaultInter, result[3]);
        Assert.Equal(DefaultInter, result[4]);
        Assert.Equal(DefaultInter, result[5]);
    }

    [Fact]
    public void Resolve4x4_ShouldRejectPpsReferencingDifferentSps()
    {
        Assert.Throws<FormatException>(() =>
            H264ScalingMatrixResolver.Resolve4x4(CreateSps(), CreatePps() with
            {
                SequenceParameterSetId = 1
            }));
    }

    private static H264SequenceParameterSet CreateSps() => new(
        0, 100, 40, 1, false, 0, 0, 0, 0, 0, 4,
        39, 22, true, true, 640, 368, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);

    private static IReadOnlyList<int>[] EmptyLists(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => (IReadOnlyList<int>)Array.Empty<int>())
            .ToArray();
}
