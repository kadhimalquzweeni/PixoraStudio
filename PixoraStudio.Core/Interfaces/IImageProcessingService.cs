using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Core.Interfaces;

public interface IImageProcessingService
{
    SKBitmap LoadImage(string filePath);
    SKBitmap ApplyAdjustments(SKBitmap original, ImageAdjustment adjustment);
    SKBitmap CloneImage(SKBitmap source);
    SKBitmap Crop(SKBitmap source, SKRectI bounds);
    SKBitmap Rotate(SKBitmap source, int degrees);
    SKBitmap Rotate(SKBitmap source, float degrees);
    SKBitmap Flip(SKBitmap source, bool horizontal, bool vertical);
    SKBitmap Resize(SKBitmap source, int width, int height);
    byte[] EncodeIcon(SKBitmap source, IReadOnlyCollection<int> sizes);
}
