using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264PcmMacroblockReconstructionTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void ReconstructPcm_ShouldCommitAllPlanesAndMarkMacroblockAsIntra(
        uint chromaFormat, int chromaHeight)
    {
        H264SequenceParameterSet sps = CreateSps(chromaFormat);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);
        byte[] y = Enumerable.Repeat((byte)91, 256).ToArray();
        byte[] cb = Enumerable.Repeat((byte)101, 8 * chromaHeight).ToArray();
        byte[] cr = Enumerable.Repeat((byte)111, 8 * chromaHeight).ToArray();

        reconstructor.ReconstructPcm(
            CreateHeader(0), new H264PcmMacroblock(0, y, cb, cr),
            0, 0, 2, sps, CreatePps());

        Assert.True(surface.IsMacroblockDecoded(0, 0));
        byte[] actualY = surface.CopyLumaPlane();
        byte[] actualCb = surface.CopyCbPlane();
        byte[] actualCr = surface.CopyCrPlane();
        for (int yRow = 0; yRow < 16; yRow++)
            Assert.All(actualY.Skip(yRow * surface.Width).Take(16),
                value => Assert.Equal((byte)91, value));
        for (int yRow = 0; yRow < chromaHeight; yRow++)
        {
            Assert.All(actualCb.Skip(yRow * surface.ChromaWidth).Take(8),
                value => Assert.Equal((byte)101, value));
            Assert.All(actualCr.Skip(yRow * surface.ChromaWidth).Take(8),
                value => Assert.Equal((byte)111, value));
        }
        Assert.True(surface.IsMacroblockAvailable(0, 0, 2, constrainedIntraPrediction: true));
    }

    [Fact]
    public void ReconstructPcm_ShouldRejectMismatchedAddressAndSampleSizes()
    {
        H264SequenceParameterSet sps = CreateSps(1);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);
        H264PcmMacroblock pcm = new(1, new byte[256], new byte[64], new byte[64]);

        Assert.Throws<FormatException>(() =>
            reconstructor.ReconstructPcm(CreateHeader(0), pcm, 0, 0, 0, sps, CreatePps()));
        Assert.Throws<FormatException>(() =>
            reconstructor.ReconstructPcm(CreateHeader(0),
                pcm with { MacroblockAddress = 0, LumaSamples = new byte[255] },
                0, 0, 0, sps, CreatePps()));
        Assert.False(surface.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void ReconstructPcm_ShouldRejectDuplicateAndNonPcmMacroblocks()
    {
        H264SequenceParameterSet sps = CreateSps(1);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);
        H264PcmMacroblock pcm = new(0, new byte[256], new byte[64], new byte[64]);

        reconstructor.ReconstructPcm(CreateHeader(0), pcm, 0, 0, 0, sps, CreatePps());
        Assert.Throws<InvalidOperationException>(() =>
            reconstructor.ReconstructPcm(CreateHeader(0), pcm, 0, 0, 0, sps, CreatePps()));
        Assert.Throws<ArgumentException>(() =>
            new H264IntraMacroblockReconstructor(new H264DecodedPictureSurface(sps))
                .ReconstructPcm(CreateHeader(0) with { IsPcm = false }, pcm,
                    0, 0, 0, sps, CreatePps()));
    }

    private static H264MacroblockHeader CreateHeader(uint address) =>
        new(address, 25, false, 0, true, false, null, null, null, true);

    private static H264SequenceParameterSet CreateSps(uint chromaFormat) =>
        new(0, 66, 30, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            1, 1, true, true, 32, 32, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);
}
