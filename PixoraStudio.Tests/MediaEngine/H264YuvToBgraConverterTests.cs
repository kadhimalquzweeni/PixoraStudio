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

        Assert.True(frame420.Pixels.AsSpan(8 * frame420.Stride, 4)
            .SequenceEqual(frame422.Pixels.AsSpan(8 * frame422.Stride, 4)));
        Assert.False(frame420.Pixels.AsSpan(0, 4)
            .SequenceEqual(frame420.Pixels.AsSpan(8 * frame420.Stride, 4)));
    }

    [Fact]
    public void Convert_ShouldNotModifyDecodedYuvPlanes()
    {
        H264SequenceParameterSet sps = Sps(1, fullRange: false, matrix: 6);
        H264DecodedPictureSurface surface = Surface(
            sps,
            Enumerable.Range(0, 256).Select(value => (byte)value).ToArray(),
            Enumerable.Repeat((byte)100, 64).ToArray(),
            Enumerable.Repeat((byte)150, 64).ToArray());

        byte[] lumaBefore = surface.CopyLumaPlane();
        byte[] cbBefore = surface.CopyCbPlane();
        byte[] crBefore = surface.CopyCrPlane();

        _ = new H264YuvToBgraConverter().Convert(surface, sps);

        Assert.Equal(lumaBefore, surface.CopyLumaPlane());
        Assert.Equal(cbBefore, surface.CopyCbPlane());
        Assert.Equal(crBefore, surface.CopyCrPlane());
    }

    [Fact]
    public void Convert_ShouldRejectIncompleteSurface()
    {
        H264SequenceParameterSet sps = Sps(1, false, 6);
        H264DecodedPictureSurface surface = new(sps);

        Assert.Throws<InvalidOperationException>(() =>
            new H264YuvToBgraConverter().Convert(surface, sps));
    }

    [Fact]
    public void ConvertScaled_ShouldAverageSourceRegionsIntoTheirOutputPixel()
    {
        H264SequenceParameterSet sps = Sps(1, fullRange: false, matrix: 6);
        byte[] luma = new byte[256];
        for (int row = 0; row < 16; row++)
        {
            for (int column = 0; column < 16; column++)
                luma[row * 16 + column] = column < 8 ? (byte)16 : (byte)235;
        }

        H264DecodedPictureSurface surface = Surface(sps, luma,
            Enumerable.Repeat((byte)128, 64).ToArray(),
            Enumerable.Repeat((byte)128, 64).ToArray());

        H264BgraFrame frame = new H264YuvToBgraConverter().ConvertScaled(surface, sps, 2);

        Assert.Equal(2, frame.Width);
        Assert.Equal(2, frame.Height);
        Assert.Equal(8, frame.Stride);
        Assert.Equal(16, frame.Pixels.Length);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, frame.Pixels[..4]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, frame.Pixels[4..8]);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, frame.Pixels[8..12]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, frame.Pixels[12..16]);
    }

    [Fact]
    public void ConvertScaled_ShouldApplyTheSameColourMatrixAsConvert()
    {
        H264SequenceParameterSet sps = Sps(1, fullRange: false, matrix: 6);
        H264DecodedPictureSurface surface = Surface(sps,
            Enumerable.Repeat((byte)81, 256).ToArray(),
            Enumerable.Repeat((byte)90, 64).ToArray(),
            Enumerable.Repeat((byte)240, 64).ToArray());

        H264BgraFrame frame = new H264YuvToBgraConverter().ConvertScaled(surface, sps, 4);

        Assert.Equal(4, frame.Width);
        Assert.Equal(4, frame.Height);
        for (int pixel = 0; pixel < 16; pixel++)
        {
            int offset = pixel * 4;
            Assert.InRange(frame.Pixels[offset + 2], (byte)250, (byte)255);
            Assert.InRange(frame.Pixels[offset + 1], (byte)0, (byte)5);
            Assert.InRange(frame.Pixels[offset], (byte)0, (byte)5);
            Assert.Equal(255, frame.Pixels[offset + 3]);
        }
    }

    [Fact]
    public void ConvertScaled_ShouldPreserveAspectRatio()
    {
        H264SequenceParameterSet sps = Sps(1, fullRange: false, matrix: 6);
        H264DecodedPictureSurface surface = Surface(sps,
            Enumerable.Repeat((byte)128, 256).ToArray(),
            Enumerable.Repeat((byte)128, 64).ToArray(),
            Enumerable.Repeat((byte)128, 64).ToArray());

        H264BgraFrame frame = new H264YuvToBgraConverter().ConvertScaled(surface, sps, 5);

        Assert.Equal(5, frame.Width);
        Assert.Equal(5, frame.Height);
        Assert.Equal(20, frame.Stride);
        Assert.Equal(100, frame.Pixels.Length);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    public void ConvertScaled_TargetAtOrAboveSourceWidth_ShouldMatchFullConversion(int targetWidth)
    {
        H264SequenceParameterSet sps = Sps(1, fullRange: false, matrix: 6);
        H264DecodedPictureSurface surface = Surface(sps,
            Enumerable.Range(0, 256).Select(value => (byte)value).ToArray(),
            Enumerable.Repeat((byte)100, 64).ToArray(),
            Enumerable.Repeat((byte)150, 64).ToArray());

        H264YuvToBgraConverter converter = new();
        H264BgraFrame full = converter.Convert(surface, sps);
        H264BgraFrame scaled = converter.ConvertScaled(surface, sps, targetWidth);

        Assert.Equal(full.Width, scaled.Width);
        Assert.Equal(full.Height, scaled.Height);
        Assert.Equal(full.Pixels, scaled.Pixels);
    }

    [Fact]
    public void ConvertScaled_ShouldRejectNonPositiveWidthAndIncompleteSurface()
    {
        H264SequenceParameterSet sps = Sps(1, false, 6);
        H264DecodedPictureSurface complete = Surface(sps,
            Enumerable.Repeat((byte)128, 256).ToArray(),
            Enumerable.Repeat((byte)128, 64).ToArray(),
            Enumerable.Repeat((byte)128, 64).ToArray());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new H264YuvToBgraConverter().ConvertScaled(complete, sps, 0));
        Assert.Throws<InvalidOperationException>(() =>
            new H264YuvToBgraConverter().ConvertScaled(new H264DecodedPictureSurface(sps), sps, 4));
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
