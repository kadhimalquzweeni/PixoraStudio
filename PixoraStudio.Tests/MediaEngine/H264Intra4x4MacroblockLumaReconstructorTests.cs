using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Intra4x4MacroblockLumaReconstructorTests
{
    [Fact]
    public void Reconstruct_ShouldProcessAllSixteenBlocksAndUseDcWhenReferencesAreUnavailable()
    {
        H264SequenceParameterSet sps = CreateSps();
        var reconstructor = new H264Intra4x4MacroblockLumaReconstructor();
        var context = new H264Intra4x4PredictionContext();

        byte[] result = reconstructor.Reconstruct(
            0, 0, 1, new byte[16], new byte[16], 0,
            false, false, false, CreateModes(), CreateResiduals(),
            context, 24, sps, CreatePps());

        Assert.Equal(Enumerable.Repeat((byte)128, 256), result);
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            Assert.Equal(2u, context.GetPredictedMode(x, y, 1));
    }

    [Fact]
    public void Reconstruct_ShouldUseReconstructedTopAndLeftSamplesForLaterBlocks()
    {
        H264SequenceParameterSet sps = CreateSps();
        var modes = new H264IntraPredictionModeSyntax(false,
            Enumerable.Range(0, 16).Select(_ =>
                new H264IntraPredictionModeEntry(false, 0u)).ToArray());
        var residuals = CreateResiduals();
        var reconstructor = new H264Intra4x4MacroblockLumaReconstructor();

        byte[] result = reconstructor.Reconstruct(
            0, 0, 1, Enumerable.Repeat((byte)70, 16).ToArray(),
            Enumerable.Repeat((byte)90, 16).ToArray(), 80,
            true, true, true, modes, residuals,
            new H264Intra4x4PredictionContext(), 24, sps, CreatePps());

        Assert.All(result.Take(4), sample => Assert.Equal((byte)70, sample));
        Assert.All(result.Skip(4).Take(4), sample => Assert.Equal((byte)70, sample));
        Assert.All(result.Skip(16).Take(4), sample => Assert.Equal((byte)70, sample));
    }

    [Fact]
    public void Reconstruct_ShouldRejectUnavailableDirectionalReferences()
    {
        var reconstructor = new H264Intra4x4MacroblockLumaReconstructor();

        Assert.Throws<FormatException>(() => reconstructor.Reconstruct(
            0, 0, 0, new byte[16], new byte[16], 0,
            false, false, false, CreateModes(0), CreateResiduals(),
            new H264Intra4x4PredictionContext(), 24, CreateSps(), CreatePps()));
    }

    [Fact]
    public void Reconstruct_ShouldRejectMalformedResidualGridAndTransformSize()
    {
        var reconstructor = new H264Intra4x4MacroblockLumaReconstructor();
        var context = new H264Intra4x4PredictionContext();

        Assert.Throws<FormatException>(() => reconstructor.Reconstruct(
            0, 0, 0, new byte[16], new byte[16], 0,
            false, false, false, CreateModes(),
            CreateResiduals().Take(15).ToArray(), context, 24, CreateSps(), CreatePps()));

        Assert.Throws<NotSupportedException>(() => reconstructor.Reconstruct(
            0, 0, 0, new byte[16], new byte[16], 0,
            false, false, false,
            new H264IntraPredictionModeSyntax(true, new H264IntraPredictionModeEntry[4]),
            CreateResiduals(), context, 24, CreateSps(), CreatePps()));
    }

    private static H264IntraPredictionModeSyntax CreateModes(uint remainder = 2) =>
        new(false, Enumerable.Range(0, 16)
            .Select(_ => new H264IntraPredictionModeEntry(false, remainder)).ToArray());

    private static H264IntraLumaResidualBlock[] CreateResiduals()
    {
        int[] xs = [0, 1, 0, 1, 2, 3, 2, 3, 0, 1, 0, 1, 2, 3, 2, 3];
        int[] ys = [0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 3, 3, 2, 2, 3, 3];
        return Enumerable.Range(0, 16).Select(i =>
            new H264IntraLumaResidualBlock(xs[i], ys[i], false,
                new H264CavlcResidualBlock(16, 0, 0, 0, new int[16]))).ToArray();
    }

    private static H264SequenceParameterSet CreateSps() =>
        new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);
}
