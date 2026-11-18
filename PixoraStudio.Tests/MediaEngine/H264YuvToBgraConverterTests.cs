using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264YuvToBgraConverterTests
{
    [Fact]
    public void Convert_LimitedRangeBlackAndWhite_ShouldProduceOpaqueBgraPixels()
    {
        H264SequenceParameterSet sps = Sps(1, fullRange: false, matrix: 6);
        byte[] luma = Enumerable.Repeat((byte)16, 256).ToArray();
        luma[^1] = 235;
        H264DecodedPictureSurface surface = Surface(sps, luma,
            Enumerable.Repeat((byte)128, 64).ToArray(),
            Enumerable.Repeat((byte)128, 64).ToArray());

        H264BgraFrame frame = new H264YuvToBgraConverter().Convert(surface, sps);

        Assert.Equal(16, frame.Width);
        Assert.Equal(16, frame.Height);
        Assert.Equal(64, frame.Stride);
        Assert.Equal(1024, frame.Pixels.Length);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, frame.Pixels[..4]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, frame.Pixels[^4..]);
    }

    [Fact]
    public void Convert_FullRange_ShouldNotApplyStudioRangeOffsets()
    {
        H264SequenceParameterSet sps = Sps(1, fullRange: true, matrix: 1);
        byte[] luma = Enumerable.Repeat((byte)0, 256).ToArray();
        luma[^1] = 255;
        H264DecodedPictureSurface surface = Surface(sps, luma,
            Enumerable.Repeat((byte)128, 64).ToArray(),
            Enumerable.Repeat((byte)128, 64).ToArray());

        H264BgraFrame frame = new H264YuvToBgraConverter().Convert(surface, sps);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, frame.Pixels[..4]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, frame.Pixels[^4..]);
    }

    [Fact]
    public void Convert_LimitedRangeBt601Red_ShouldUseCorrectMatrixAndChromaScaling()
    {
        H264SequenceParameterSet sps = Sps(1, fullRange: false, matrix: 6);
        H264DecodedPictureSurface surface = Surface(sps,
            Enumerable.Repeat((byte)81, 256).ToArray(),
            Enumerable.Repeat((byte)90, 64).ToArray(),
            Enumerable.Repeat((byte)240, 64).ToArray());

        H264BgraFrame frame = new H264YuvToBgraConverter().Convert(surface, sps);

        Assert.InRange(frame.Pixels[2], (byte)250, (byte)255);
        Assert.InRange(frame.Pixels[1], (byte)0, (byte)5);
        Assert.InRange(frame.Pixels[0], (byte)0, (byte)5);
        Assert.Equal(255, frame.Pixels[3]);
    }

    [Fact]
    public void Convert_420And422_ShouldSampleChromaRowsUsingTheirSubsamplingGeometry()
    {
        H264SequenceParameterSet sps420 = Sps(1, false, 6);
        H264SequenceParameterSet sps422 = Sps(2, false, 6);
        H264DecodedPictureSurface surface420 = Surface(
            sps420,
            Enumerable.Repeat((byte)128, 256).ToArray(),
            Enumerable.Range(0, 8).SelectMany(row =>
                Enumerable.Repeat(row < 4 ? (byte)128 : (byte)255, 8)).ToArray(),
            Enumerable.Repeat((byte)128, 64).ToArray());
        H264DecodedPictureSurface surface422 = Surface(
            sps422,
            Enumerable.Repeat((byte)128, 256).ToArray(),
            Enumerable.Range(0, 16).SelectMany(row =>
                Enumerable.Repeat(row < 8 ? (byte)128 : (byte)255, 8)).ToArray(),
            Enumerable.Repeat((byte)128, 128).ToArray());

        H264YuvToBgraConverter converter = new();
        H264BgraFrame frame420 = converter.Convert(surface420, sps420);
        H264BgraFrame frame422 = converter.Convert(surface422, sps422);

        Assert.Equal(frame420.Pixels[(8 * frame420.Stride)..(8 * frame420.Stride + 4)],
            frame422.Pixels[(8 * frame422.Stride)..(8 * frame422.Stride + 4)]);
        Assert.NotEqual(frame420.Pixels[..4], frame420.Pixels[(8 * frame420.Stride)..(8 * frame420.Stride + 4)]);
    }

    [Fact]
    public void Convert_ShouldRejectIncompleteSurface()
    {
        H264SequenceParameterSet sps = Sps(1, false, 6);
        H264DecodedPictureSurface surface = new(sps);

        Assert.Throws<InvalidOperationException>(() =>
            new H264YuvToBgraConverter().Convert(surface, sps));
    }

    private static H264SequenceParameterSet Sps(
        uint chromaFormat,
        bool fullRange,
        uint matrix)
    {
        H264VuiParameters vui = new(
            1, null, null, false, null, fullRange,
            null, null, matrix, null, null, null, null, false,
            null, null, false, false, false, false,
            null, null, null, null, null, null);
        return new H264SequenceParameterSet(
            0, 66, 30, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, true)
        { VuiParameters = vui };
    }

    private static H264DecodedPictureSurface Surface(
        H264SequenceParameterSet sps,
        byte[] luma,
        byte[] cb,
        byte[] cr)
    {
        H264DecodedPictureSurface surface = new(sps);
        surface.WriteMacroblock(0, 0, 0, false, luma, cb, cr);
        return surface;
    }
}
