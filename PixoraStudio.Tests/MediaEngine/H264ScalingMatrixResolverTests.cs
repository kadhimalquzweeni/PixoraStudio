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
    public void Resolve4x4_ShouldUseSequenceLumaAnchorsForMissingPpsLists()
    {
        IReadOnlyList<int>[] spsLists = EmptyLists(8);
        spsLists[0] = Enumerable.Repeat(22, 16).ToArray();
        spsLists[3] = Enumerable.Repeat(44, 16).ToArray();
        H264SequenceParameterSet sps = CreateSps() with
        {
            ScalingMatrixPresentFlag = true,
            ScalingLists = spsLists,
            ScalingListUseDefault = new bool[8]
        };
        H264PictureParameterSetHeader pps = CreatePps() with
        {
            PicScalingMatrixPresentFlag = true,
            ScalingLists = EmptyLists(6),
            ScalingListUseDefault = new bool[6]
        };

        int[][] result = H264ScalingMatrixResolver.Resolve4x4(sps, pps);

        Assert.Equal(22, Assert.Single(result[0].Distinct()));
        Assert.Equal(result[0], result[1]);
        Assert.Equal(result[1], result[2]);
        Assert.Equal(44, Assert.Single(result[3].Distinct()));
        Assert.Equal(result[3], result[4]);
        Assert.Equal(result[4], result[5]);
    }

    [Fact]
    public void Resolve4x4_ShouldUseFallbackRuleAWhenSpsScalingMatrixIsAbsent()
    {
        H264PictureParameterSetHeader pps = CreatePps() with
        {
            PicScalingMatrixPresentFlag = true,
            ScalingLists = EmptyLists(6),
            ScalingListUseDefault = new bool[6]
        };

        int[][] result = H264ScalingMatrixResolver.Resolve4x4(CreateSps(), pps);

        Assert.Equal(DefaultIntra, result[0]);
        Assert.Equal(DefaultIntra, result[1]);
        Assert.Equal(DefaultIntra, result[2]);
        Assert.Equal(DefaultInter, result[3]);
        Assert.Equal(DefaultInter, result[4]);
        Assert.Equal(DefaultInter, result[5]);
    }

    [Fact]
    public void Resolve4x4_ShouldApplyPpsOverridesAndSpsFallbacks()
    {
        IReadOnlyList<int>[] spsLists = EmptyLists(8);
        spsLists[0] = Enumerable.Repeat(22, 16).ToArray();
        spsLists[1] = Enumerable.Repeat(55, 16).ToArray();
        spsLists[3] = Enumerable.Repeat(44, 16).ToArray();
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
        // PPS list 1 chains from its PPS list 0, not the SPS list 1.
        Assert.Equal(31, Assert.Single(result[1].Distinct()));
        Assert.Equal(result[1], result[2]);
        // PPS list 3 uses the SPS-level inter anchor (index 3).
        Assert.Equal(44, Assert.Single(result[3].Distinct()));
        Assert.Equal(result[3], result[4]);
        Assert.Equal(result[3], result[5]);
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


    private static readonly int[] DefaultIntra8x8 =
    [
        6,10,13,16,18,23,25,27, 10,11,16,18,23,25,27,29,
        13,16,18,23,25,27,29,31, 16,18,23,25,27,29,31,33,
        18,23,25,27,29,31,33,36, 23,25,27,29,31,33,36,38,
        25,27,29,31,33,36,38,40, 27,29,31,33,36,38,40,42
    ];

    private static readonly int[] DefaultInter8x8 =
    [
        9,13,15,17,19,21,22,24, 13,13,17,19,21,22,24,25,
        15,17,19,21,22,24,25,27, 17,19,21,22,24,25,27,28,
        19,21,22,24,25,27,28,30, 21,22,24,25,27,28,30,32,
        22,24,25,27,28,30,32,33, 24,25,27,28,30,32,33,35
    ];

    [Fact]
    public void Resolve8x8_ShouldReturnFlatMatricesWhenScalingMatricesAreAbsent()
    {
        var pps = CreatePps() with { Transform8X8ModeFlag = true };
        int[][] result = H264ScalingMatrixResolver.Resolve8x8(CreateSps(), pps);
        Assert.Equal(2, result.Length);
        Assert.All(result, matrix => Assert.Equal(Enumerable.Repeat(16, 64), matrix));
    }

    [Fact]
    public void Resolve8x8_ShouldUseNormativeDefaultMatricesWhenPpsSignalsUseDefault()
    {
        IReadOnlyList<int>[] lists = EmptyLists(8);
        lists[6] = Enumerable.Repeat(8, 64).ToArray();
        lists[7] = Enumerable.Repeat(9, 64).ToArray();
        bool[] useDefault = new bool[8];
        useDefault[6] = useDefault[7] = true;
        var pps = CreatePps() with
        {
            Transform8X8ModeFlag = true, PicScalingMatrixPresentFlag = true,
            ScalingLists = lists, ScalingListUseDefault = useDefault
        };
        int[][] result = H264ScalingMatrixResolver.Resolve8x8(CreateSps(), pps);
        Assert.Equal(DefaultIntra8x8, result[0]);
        Assert.Equal(DefaultInter8x8, result[1]);
    }

    [Fact]
    public void Resolve8x8_ShouldConvertExplicitPpsListsFromScanToRasterOrder()
    {
        IReadOnlyList<int>[] lists = EmptyLists(8);
        int[] scan = Enumerable.Range(1, 64).ToArray();
        lists[6] = scan;
        var pps = CreatePps() with
        {
            Transform8X8ModeFlag = true, PicScalingMatrixPresentFlag = true,
            ScalingLists = lists, ScalingListUseDefault = new bool[8]
        };
        int[][] result = H264ScalingMatrixResolver.Resolve8x8(CreateSps(), pps);
        Assert.Equal(H264CoefficientScanner.ToRasterOrder(scan), result[0]);
        Assert.Equal(DefaultInter8x8, result[1]);
    }

    [Fact]
    public void Resolve8x8_ShouldInheritSequenceListsForMissingPpsLists()
    {
        IReadOnlyList<int>[] lists = EmptyLists(8);
        lists[6] = Enumerable.Repeat(37, 64).ToArray();
        lists[7] = Enumerable.Repeat(53, 64).ToArray();
        var sps = CreateSps() with
        {
            ScalingMatrixPresentFlag = true, ScalingLists = lists,
            ScalingListUseDefault = new bool[8]
        };
        var pps = CreatePps() with
        {
            Transform8X8ModeFlag = true, PicScalingMatrixPresentFlag = true,
            ScalingLists = EmptyLists(8), ScalingListUseDefault = new bool[8]
        };
        int[][] result = H264ScalingMatrixResolver.Resolve8x8(sps, pps);
        Assert.Equal(37, Assert.Single(result[0].Distinct()));
        Assert.Equal(53, Assert.Single(result[1].Distinct()));
    }

    [Fact]
    public void Resolve8x8_ShouldResolveSixMatricesForChromaFormat444()
    {
        var sps = CreateSps() with { ChromaFormatIdc = 3 };
        var pps = CreatePps() with
        {
            Transform8X8ModeFlag = true, PicScalingMatrixPresentFlag = true,
            ScalingLists = EmptyLists(12), ScalingListUseDefault = new bool[12]
        };
        int[][] result = H264ScalingMatrixResolver.Resolve8x8(sps, pps);
        Assert.Equal(6, result.Length);
        for (int i = 0; i < result.Length; i++)
            Assert.Equal((i & 1) == 0 ? DefaultIntra8x8 : DefaultInter8x8, result[i]);
    }

    [Fact]
    public void Resolve8x8_ShouldRejectWhenTransform8x8ModeIsDisabled()
    {
        Assert.Throws<FormatException>(() =>
            H264ScalingMatrixResolver.Resolve8x8(CreateSps(), CreatePps()));
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
