using PixoraStudio.Infrastructure.Services;
using SkiaSharp;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class ImageEncodingTests
{
    private readonly ImageProcessingService _service = new();

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    public void EncodeImage_ShouldProduceDecodableImage(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(8, 6);
        bitmap.Erase(SKColors.CornflowerBlue);

        byte[] encoded = _service.EncodeImage(bitmap, format);

        Assert.NotEmpty(encoded);
        using var decoded = SKBitmap.Decode(encoded) ?? throw new InvalidOperationException("Encoded image could not be decoded.");
        Assert.Equal(8, decoded.Width);
        Assert.Equal(6, decoded.Height);
    }

    [Fact]
    public void EncodeImage_ShouldRejectInvalidQuality()
    {
        using var bitmap = new SKBitmap(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.EncodeImage(bitmap, SKEncodedImageFormat.Jpeg, 0));
    }

    [Fact]
    public void EncodeImage_ShouldRejectUnsupportedFormat()
    {
        using var bitmap = new SKBitmap(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.EncodeImage(bitmap, SKEncodedImageFormat.Bmp));
    }
}
