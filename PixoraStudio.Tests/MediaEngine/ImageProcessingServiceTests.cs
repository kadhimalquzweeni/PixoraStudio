using PixoraStudio.Infrastructure.Services;
using SkiaSharp;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class ImageProcessingServiceTests
{
    private readonly ImageProcessingService _service = new();

    private static SKBitmap CreatePattern()
    {
        var bitmap = new SKBitmap(2, 3);
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Green);
        bitmap.SetPixel(0, 1, SKColors.Blue);
        bitmap.SetPixel(1, 1, SKColors.Yellow);
        bitmap.SetPixel(0, 2, SKColors.Magenta);
        bitmap.SetPixel(1, 2, SKColors.Cyan);
        return bitmap;
    }

    [Fact]
    public void Crop_ShouldReturnRequestedRegion()
    {
        using var source = CreatePattern();
        using var result = _service.Crop(source, new SKRectI(1, 1, 2, 3));

        Assert.Equal(1, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(SKColors.Yellow, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Cyan, result.GetPixel(0, 1));
    }

    [Theory]
    [InlineData(-1, 0, 2, 2)]
    [InlineData(0, 0, 0, 2)]
    [InlineData(1, 1, 3, 3)]
    public void Crop_ShouldRejectInvalidBounds(int left, int top, int right, int bottom)
    {
        using var source = CreatePattern();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Crop(source, new SKRectI(left, top, right, bottom)));
    }

    [Fact]
    public void RotateClockwise_ShouldSwapDimensionsAndMovePixels()
    {
        using var source = CreatePattern();
        using var result = _service.Rotate(source, 90);

        Assert.Equal(3, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(SKColors.Magenta, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, result.GetPixel(2, 0));
        Assert.Equal(SKColors.Cyan, result.GetPixel(0, 1));
        Assert.Equal(SKColors.Green, result.GetPixel(2, 1));
    }

    [Fact]
    public void Rotate180_ShouldPreserveDimensionsAndReversePixels()
    {
        using var source = CreatePattern();
        using var result = _service.Rotate(source, 180);

        Assert.Equal(2, result.Width);
        Assert.Equal(3, result.Height);
        Assert.Equal(SKColors.Cyan, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, result.GetPixel(1, 2));
    }

    [Fact]
    public void Rotate_ShouldRejectUnsupportedAngles()
    {
        using var source = CreatePattern();
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Rotate(source, 45));
    }

    [Fact]
    public void FlipHorizontal_ShouldReverseEachRow()
    {
        using var source = CreatePattern();
        using var result = _service.Flip(source, horizontal: true, vertical: false);

        Assert.Equal(SKColors.Green, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, result.GetPixel(1, 0));
        Assert.Equal(SKColors.Cyan, result.GetPixel(0, 2));
        Assert.Equal(SKColors.Magenta, result.GetPixel(1, 2));
    }

    [Fact]
    public void FlipVertical_ShouldReverseRowOrder()
    {
        using var source = CreatePattern();
        using var result = _service.Flip(source, horizontal: false, vertical: true);

        Assert.Equal(SKColors.Magenta, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, result.GetPixel(0, 2));
    }

    [Fact]
    public void Resize_ShouldReturnRequestedDimensions()
    {
        using var source = CreatePattern();
        using var result = _service.Resize(source, 8, 6);

        Assert.Equal(8, result.Width);
        Assert.Equal(6, result.Height);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void Resize_ShouldRejectNonPositiveDimensions(int width, int height)
    {
        using var source = CreatePattern();
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Resize(source, width, height));
    }

    [Theory]
    [InlineData(45)]
    [InlineData(-12.5)]
    [InlineData(135)]
    public void Rotate_FreeAngle_ShouldExpandCanvasAndPreserveSourceCentre(float angle)
    {
        using var source = new SKBitmap(20, 10);
        source.Erase(SKColors.Red);

        using var result = _service.Rotate(source, angle);

        Assert.True(result.Width >= source.Width);
        Assert.True(result.Height >= source.Height);
        Assert.Equal(SKColors.Red, result.GetPixel(result.Width / 2, result.Height / 2));
    }

    [Fact]
    public void Rotate_FullTurn_ShouldReturnSameDimensions()
    {
        using var source = CreatePattern();
        using var result = _service.Rotate(source, 360f);

        Assert.Equal(source.Width, result.Width);
        Assert.Equal(source.Height, result.Height);
    }

    [Fact]
    public void Rotate_ShouldRejectNonFiniteAngle()
    {
        using var source = CreatePattern();

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Rotate(source, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Rotate(source, float.PositiveInfinity));
    }

    [Fact]
    public void ApplyAdjustments_NeutralSettings_ShouldPreservePixelsAndAlpha()
    {
        using var source = new SKBitmap(1, 1);
        source.SetPixel(0, 0, new SKColor(80, 120, 160, 90));

        using var result = _service.ApplyAdjustments(source, new PixoraStudio.Core.Models.ImageAdjustment());

        Assert.Equal(source.GetPixel(0, 0), result.GetPixel(0, 0));
    }

    [Theory]
    [InlineData(1f, true)]
    [InlineData(-1f, false)]
    public void ApplyAdjustments_Exposure_ShouldChangeOverallBrightness(float exposure, bool shouldBrighten)
    {
        using var source = new SKBitmap(1, 1);
        source.SetPixel(0, 0, new SKColor(60, 80, 100));
        using var result = _service.ApplyAdjustments(source,
            new PixoraStudio.Core.Models.ImageAdjustment { Exposure = exposure });

        int sourceLuma = source.GetPixel(0, 0).Red + source.GetPixel(0, 0).Green + source.GetPixel(0, 0).Blue;
        SKColor output = result.GetPixel(0, 0);
        int resultLuma = output.Red + output.Green + output.Blue;
        Assert.Equal(shouldBrighten, resultLuma > sourceLuma);
    }

    [Fact]
    public void ApplyAdjustments_Highlights_ShouldAffectBrightPixelsMoreThanDarkPixels()
    {
        using var source = new SKBitmap(2, 1);
        source.SetPixel(0, 0, new SKColor(30, 30, 30));
        source.SetPixel(1, 0, new SKColor(220, 220, 220));
        using var result = _service.ApplyAdjustments(source,
            new PixoraStudio.Core.Models.ImageAdjustment { Highlights = -100f });

        int darkPixelChange = 30 - result.GetPixel(0, 0).Red;
        int brightPixelChange = 220 - result.GetPixel(1, 0).Red;
        Assert.True(brightPixelChange > darkPixelChange);
    }

    [Fact]
    public void ApplyAdjustments_Shadows_ShouldAffectDarkPixelsMoreThanBrightPixels()
    {
        using var source = new SKBitmap(2, 1);
        source.SetPixel(0, 0, new SKColor(30, 30, 30));
        source.SetPixel(1, 0, new SKColor(220, 220, 220));
        using var result = _service.ApplyAdjustments(source,
            new PixoraStudio.Core.Models.ImageAdjustment { Shadows = 100f });

        int darkPixelChange = result.GetPixel(0, 0).Red - 30;
        int brightPixelChange = result.GetPixel(1, 0).Red - 220;
        Assert.True(darkPixelChange > brightPixelChange);
    }

    [Theory]
    [InlineData(100f, true)]
    [InlineData(-100f, false)]
    public void ApplyAdjustments_Temperature_ShouldShiftRedBlueBalance(float temperature, bool shouldWarm)
    {
        using var source = new SKBitmap(1, 1);
        source.SetPixel(0, 0, new SKColor(100, 100, 100));
        using var result = _service.ApplyAdjustments(source,
            new PixoraStudio.Core.Models.ImageAdjustment { Temperature = temperature });
        SKColor output = result.GetPixel(0, 0);

        Assert.Equal(shouldWarm, output.Red > output.Blue);
    }

    [Theory]
    [InlineData(100f, true)]
    [InlineData(-100f, false)]
    public void ApplyAdjustments_Tint_ShouldShiftGreenAgainstRedAndBlue(float tint, bool shouldBeMagenta)
    {
        using var source = new SKBitmap(1, 1);
        source.SetPixel(0, 0, new SKColor(100, 100, 100));
        using var result = _service.ApplyAdjustments(source,
            new PixoraStudio.Core.Models.ImageAdjustment { Tint = tint });
        SKColor output = result.GetPixel(0, 0);

        Assert.Equal(shouldBeMagenta, output.Green < output.Red && output.Green < output.Blue);
    }

    [Fact]
    public void ApplyAdjustments_ShouldPreserveSourceAlphaAndNotMutateInput()
    {
        using var source = new SKBitmap(1, 1);
        var original = new SKColor(40, 80, 120, 73);
        source.SetPixel(0, 0, original);
        SKColor sourcePixelBeforeProcessing = source.GetPixel(0, 0);

        using var result = _service.ApplyAdjustments(source,
            new PixoraStudio.Core.Models.ImageAdjustment { Exposure = 1f, Temperature = 20f });

        Assert.Equal(sourcePixelBeforeProcessing, source.GetPixel(0, 0));
        Assert.Equal((byte)73, result.GetPixel(0, 0).Alpha);
    }
}
