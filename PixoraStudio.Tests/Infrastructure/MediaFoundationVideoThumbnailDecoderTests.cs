using PixoraStudio.Infrastructure.Services;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class MediaFoundationVideoThumbnailDecoderTests
{
    [Fact]
    public void GetReadDisposition_ShouldAcceptTargetSampleEvenWhenEndOfStreamIsAlsoSet()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, false, true, 30_000_000, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Accept, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldAcceptSampleAfterTargetEvenWhenEndOfStreamIsSet()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, false, true, 30_500_000, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Accept, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldSkipPrerollSampleBeforeTarget()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, false, false, 29_000_000, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Skip, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldSkipEmptyReadWithoutEndOfStream()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            false, false, false, 0, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Skip, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldStopWhenEndOfStreamHasNoUsableSample()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            false, false, true, 0, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.EndOfStream, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldStopWhenEndOfStreamSampleIsBeforeTarget()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, false, true, 29_000_000, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.EndOfStream, result);
    }

    [Fact]
    public void GetReadDisposition_ShouldPrioritiseDecodingErrors()
    {
        var result = MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
            true, true, true, 30_000_000, 30_000_000);

        Assert.Equal(MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Error, result);
    }
}
