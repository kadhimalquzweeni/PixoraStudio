using PixoraStudio.Infrastructure.Services;
using SkiaSharp;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class IconEncodingTests
{
    private readonly ImageProcessingService _service = new();

    [Fact]
    public void EncodeIcon_ShouldCreateMultiResolutionIcoWithSelectedSizes()
    {
        using var source = new SKBitmap(80, 40);
        source.Erase(new SKColor(220, 40, 30, 255));

        byte[] icon = _service.EncodeIcon(source, [16, 32, 48, 256]);

        Assert.Equal((ushort)0, BitConverter.ToUInt16(icon, 0));
        Assert.Equal((ushort)1, BitConverter.ToUInt16(icon, 2));
        Assert.Equal((ushort)4, BitConverter.ToUInt16(icon, 4));

        int[] expectedSizes = [16, 32, 48, 256];
        for (int i = 0; i < expectedSizes.Length; i++)
        {
            int entryOffset = 6 + i * 16;
            int expected = expectedSizes[i];
            Assert.Equal(expected == 256 ? 0 : expected, icon[entryOffset]);
            Assert.Equal(expected == 256 ? 0 : expected, icon[entryOffset + 1]);
            Assert.Equal((ushort)1, BitConverter.ToUInt16(icon, entryOffset + 4));
            Assert.Equal((ushort)32, BitConverter.ToUInt16(icon, entryOffset + 6));

            int byteCount = checked((int)BitConverter.ToUInt32(icon, entryOffset + 8));
            int imageOffset = checked((int)BitConverter.ToUInt32(icon, entryOffset + 12));
            using var decoded = SKBitmap.Decode(icon.AsSpan(imageOffset, byteCount).ToArray());
            Assert.NotNull(decoded);
            Assert.Equal(expected, decoded!.Width);
            Assert.Equal(expected, decoded.Height);
        }
    }

    [Fact]
    public void EncodeIcon_ShouldSortAndRemoveDuplicateSizes()
    {
        using var source = new SKBitmap(32, 32);
        source.Erase(SKColors.Blue);

        byte[] icon = _service.EncodeIcon(source, [64, 16, 32, 16]);

        Assert.Equal((ushort)3, BitConverter.ToUInt16(icon, 4));
        Assert.Equal(16, icon[6]);
        Assert.Equal(32, icon[22]);
        Assert.Equal(64, icon[38]);
    }

    [Fact]
    public void EncodeIcon_ShouldPreserveTransparencyAndContainImageWithoutDistortion()
    {
        using var source = new SKBitmap(40, 20);
        source.Erase(SKColors.Red);

        byte[] icon = _service.EncodeIcon(source, [32]);
        int imageOffset = checked((int)BitConverter.ToUInt32(icon, 18));
        int byteCount = checked((int)BitConverter.ToUInt32(icon, 14));
        using var decoded = SKBitmap.Decode(icon.AsSpan(imageOffset, byteCount).ToArray());

        Assert.NotNull(decoded);
        Assert.Equal((byte)0, decoded!.GetPixel(16, 0).Alpha);
        Assert.Equal((byte)0, decoded.GetPixel(16, 31).Alpha);
        Assert.Equal((byte)255, decoded.GetPixel(16, 16).Alpha);
        Assert.Equal((byte)255, decoded.GetPixel(8, 16).Alpha);
    }

    [Fact]
    public void EncodeIcon_ShouldRejectEmptySizeCollection()
    {
        using var source = new SKBitmap(10, 10);
        Assert.Throws<ArgumentException>(() => _service.EncodeIcon(source, []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(257)]
    public void EncodeIcon_ShouldRejectSizesOutsideSupportedRange(int size)
    {
        using var source = new SKBitmap(10, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.EncodeIcon(source, [size]));
    }
}
