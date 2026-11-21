using PixoraStudio.Infrastructure.Services;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class MediaFoundationVideoThumbnailDecoderTests
{
    [Fact]
    public void GetReadDisposition_ShouldAcceptTargetSample()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, false, false, false, 30_000_000, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Accept, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldAcceptSampleAfterTarget()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, false, false, false, 30_500_000, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Accept, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldSkipPrerollSampleBeforeTarget()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, false, false, false, 29_000_000, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Skip, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldSkipEmptyReadWithoutEndOfStream()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            false, false, false, false, 0, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Skip, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldUseCurrentFinalSampleWhenItPrecedesTarget()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, false, false, true, 29_000_000, 30_000_000);

        Assert.Equal(
            MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.AcceptLastAvailable,
            result);
    }

    [Fact]
    public void GetReadDisposition_ShouldUsePreviousSampleWhenEndOfStreamReturnsNoSample()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            false, true, false, true, 0, 30_000_000);

        Assert.Equal(
            MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.AcceptLastAvailable,
            result);
    }

    [Fact]
    public void GetReadDisposition_ShouldStopWhenEndOfStreamHasNoAvailableSample()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            false, false, false, true, 0, 30_000_000);

        Assert.Equal(
            MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.EndOfStream,
            result);
    }

    [Fact]
    public void GetReadDisposition_ShouldPrioritiseDecodingErrors()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, true, true, true, 30_000_000, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Error, result);
    }
}
