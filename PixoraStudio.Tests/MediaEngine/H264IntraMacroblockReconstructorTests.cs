using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraMacroblockReconstructorTests
{
    [Theory]
    [InlineData(1, 64)]
    [InlineData(2, 128)]
    public void ReconstructIntra16x16_ShouldWriteLumaAndBothChromaPlanes(
        uint format, int chromaCount)
    {
        var sps = CreateSps(format);
        var pps = CreatePps();
        var surface = new H264DecodedPictureSurface(sps);
        var reconstructor = new H264IntraMacroblockReconstructor(surface);
        var header = CreateHeader(0, 2);
        var syntax = CreateSyntax(0);
        var residual = CreateResidualMacroblock(0, format);

        reconstructor.ReconstructIntra16x16(
            0, 0, 0, header, syntax, residual, 24, sps, pps);

        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.All(surface.CopyLumaPlane(), value => Assert.Equal((byte)128, value));
        Assert.Equal(chromaCount, surface.CopyCbPlane().Length);
        Assert.All(surface.CopyCbPlane(), value => Assert.Equal((byte)128, value));
        Assert.All(surface.CopyCrPlane(), value => Assert.Equal((byte)128, value));
    }

    [Fact]
    public void ReconstructIntra16x16_ShouldUsePreviouslyReconstructedTopAndLeftReferences()
    {
        var sps = CreateSps(1);
        var surface = new H264DecodedPictureSurface(sps);
        var reconstructor = new H264IntraMacroblockReconstructor(surface);

        // Decode top-left first, then the macroblock above and to the left of
        // the target so its external top and left references are available.
        reconstructor.ReconstructIntra16x16(0, 0, 0, CreateHeader(0, 2),
            CreateSyntax(0), CreateResidualMacroblock(0, 1), 24, sps, CreatePps());
        reconstructor.ReconstructIntra16x16(1, 0, 0, CreateHeader(1, 2),
            CreateSyntax(0), CreateResidualMacroblock(1, 1), 24, sps, CreatePps());
        reconstructor.ReconstructIntra16x16(0, 1, 0, CreateHeader(2, 2),
            CreateSyntax(0), CreateResidualMacroblock(2, 1), 24, sps, CreatePps());

        reconstructor.ReconstructIntra16x16(1, 1, 0, CreateHeader(3, 2),
            CreateSyntax(0), CreateResidualMacroblock(3, 1), 24, sps, CreatePps());

        Assert.True(surface.IsMacroblockDecoded(1, 1));
        Assert.Equal((byte)128, surface.GetLumaSample(16, 16));
    }

    [Fact]
    public void ReconstructIntra16x16_ShouldRejectMissingPlanePredictionReferences()
    {
        var sps = CreateSps(1);
        var surface = new H264DecodedPictureSurface(sps);
        var reconstructor = new H264IntraMacroblockReconstructor(surface);

        Assert.Throws<FormatException>(() => reconstructor.ReconstructIntra16x16(
            0, 0, 0, CreateHeader(0, 3), CreateSyntax(0),
            CreateResidualMacroblock(0, 1), 24, sps, CreatePps()));
        Assert.False(surface.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void ReconstructIntra16x16_ShouldRejectI4x4PcmAndCabacPaths()
    {
        var sps = CreateSps(1);
        var surface = new H264DecodedPictureSurface(sps);
        var reconstructor = new H264IntraMacroblockReconstructor(surface);
        var residual = CreateResidualMacroblock(0, 1);

        Assert.Throws<NotSupportedException>(() => reconstructor.ReconstructIntra16x16(
            0, 0, 0, CreateHeader(0, 0) with { IsIntra16x16 = false },
            CreateSyntax(0), residual, 24, sps, CreatePps()));
        Assert.Throws<NotSupportedException>(() => reconstructor.ReconstructIntra16x16(
            0, 0, 0, CreateHeader(0, 2) with { IsPcm = true },
            CreateSyntax(0), residual, 24, sps, CreatePps()));
        Assert.Throws<NotSupportedException>(() => reconstructor.ReconstructIntra16x16(
            0, 0, 0, CreateHeader(0, 2), CreateSyntax(0), residual, 24,
            sps, CreatePps() with { EntropyCodingModeFlag = true }));
    }

    [Fact]
    public void ReconstructIntra16x16_ShouldRejectAddressAndResidualShapeMismatch()
    {
        var sps = CreateSps(1);
        var surface = new H264DecodedPictureSurface(sps);
        var reconstructor = new H264IntraMacroblockReconstructor(surface);

        Assert.Throws<FormatException>(() => reconstructor.ReconstructIntra16x16(
            0, 0, 0, CreateHeader(1, 2), CreateSyntax(0),
            CreateResidualMacroblock(0, 1), 24, sps, CreatePps()));
        Assert.Throws<FormatException>(() => reconstructor.ReconstructIntra16x16(
            0, 0, 0, CreateHeader(0, 2), CreateSyntax(0),
            CreateResidualMacroblock(0, 1) with { Blocks = [] },
            24, sps, CreatePps()));
    }

    private static H264SequenceParameterSet CreateSps(uint format) =>
        new(0, 66, 30, format, false, 0, 0, 0, 0, 0, 1,
            1, 1, true, true, 32, 32, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);

    private static H264MacroblockHeader CreateHeader(uint address, uint mode) =>
        new(address, 0, false, 0, true, true, mode, 0, 0, false);

    private static H264IntraMacroblockSyntax CreateSyntax(uint chromaMode) =>
        new(null, null, chromaMode, new H264CodedBlockPattern(0, 0, 0, 1), 0);

    private static H264IntraLumaResidualMacroblock CreateResidualMacroblock(
        uint address, uint format)
    {
        H264CavlcResidualBlock dc = CreateResidual(16);
        H264IntraLumaResidualBlock[] luma = Enumerable.Range(0, 16)
            .Select(index => new H264IntraLumaResidualBlock(
                new[] { 0, 1, 0, 1, 2, 3, 2, 3, 0, 1, 0, 1, 2, 3, 2, 3 }[index],
                new[] { 0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 3, 3, 2, 2, 3, 3 }[index],
                false, CreateResidual(15)))
            .ToArray();
        int blocks = format == 1 ? 4 : 8;
        H264IntraChromaResidualComponent[] chroma =
        [
            CreateChroma(1, blocks, format),
            CreateChroma(2, blocks, format)
        ];
        return new H264IntraLumaResidualMacroblock(address, dc, luma)
        {
            ChromaComponents = chroma
        };
    }

    private static H264IntraChromaResidualComponent CreateChroma(
        byte component, int count, uint format) =>
        new(component, CreateResidual(format == 1 ? 4 : 8),
            Enumerable.Range(0, count).Select(index =>
                new H264IntraChromaResidualBlock(index % 2, index / 2,
                    false, CreateResidual(15))).ToArray());

    private static H264CavlcResidualBlock CreateResidual(int count) =>
        new(count, 0, 0, 0, new int[count]);
}
