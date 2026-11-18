using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DecodedPictureSurfaceTests
{
    [Theory]
    [InlineData(1, 32, 16, 16, 8)]
    [InlineData(2, 32, 16, 16, 16)]
    public void Constructor_ShouldCreateCorrectPlaneGeometry(
        uint format, int width, int height, int chromaWidth, int chromaHeight)
    {
        var surface = new H264DecodedPictureSurface(CreateSps(format, 1, 0));
        Assert.Equal(width, surface.Width);
        Assert.Equal(height, surface.Height);
        Assert.Equal(chromaWidth, surface.ChromaWidth);
        Assert.Equal(chromaHeight, surface.ChromaHeight);
    }

    [Theory]
    [InlineData(1, 64)]
    [InlineData(2, 128)]
    public void WriteIntraMacroblock_ShouldCopyAllPlanesAndPublishAvailability(uint format, int chromaCount)
    {
        var surface = new H264DecodedPictureSurface(CreateSps(format, 1, 0));
        byte[] y = Enumerable.Repeat((byte)11, 256).ToArray();
        byte[] cb = Enumerable.Repeat((byte)22, chromaCount).ToArray();
        byte[] cr = Enumerable.Repeat((byte)33, chromaCount).ToArray();

        surface.WriteIntraMacroblock(0, 0, 7, y, cb, cr);

        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.True(surface.IsMacroblockAvailable(0, 0, 7, false));
        Assert.False(surface.IsMacroblockAvailable(0, 0, 8, false));
        Assert.Equal((byte)11, surface.GetLumaSample(15, 15));
        Assert.Equal((byte)22, surface.GetChromaSample(1, 7, format == 1 ? 7 : 15));
        Assert.Equal((byte)33, surface.GetChromaSample(2, 7, format == 1 ? 7 : 15));
    }

    [Fact]
    public void WriteMacroblock_ShouldDistinguishInterFromIntraForConstrainedPrediction()
    {
        var surface = new H264DecodedPictureSurface(CreateSps(1, 1, 0));
        surface.WriteMacroblock(0, 0, 2, false,
            new byte[256], new byte[64], new byte[64]);

        Assert.True(surface.IsMacroblockAvailable(0, 0, 2, false));
        Assert.False(surface.IsMacroblockAvailable(0, 0, 2, true));
    }

    [Fact]
    public void WriteIntraMacroblock_ShouldRejectWrongPlaneSizesWithoutPublishing()
    {
        var surface = new H264DecodedPictureSurface(CreateSps(1, 1, 0));
        Assert.Throws<ArgumentException>(() => surface.WriteIntraMacroblock(
            0, 0, 0, new byte[256], new byte[63], new byte[64]));
        Assert.False(surface.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void Constructor_ShouldRejectUnsupportedPictureFormats()
    {
        Assert.Throws<NotSupportedException>(() => new H264DecodedPictureSurface(
            CreateSps(3, 0, 0)));
        Assert.Throws<NotSupportedException>(() => new H264DecodedPictureSurface(
            CreateSps(1, 0, 0) with { BitDepthLumaMinus8 = 1 }));
        Assert.Throws<NotSupportedException>(() => new H264DecodedPictureSurface(
            CreateSps(1, 1, 0) with { FrameMbsOnlyFlag = false }));
    }

    private static H264SequenceParameterSet CreateSps(
        uint format, uint widthMinus1, uint heightMinus1) =>
        new(0, 66, 30, format, false, 0, 0, 0, 0, 0, 1,
            widthMinus1, heightMinus1, true, true,
            (widthMinus1 + 1) * 16, (heightMinus1 + 1) * 16, false);
}
