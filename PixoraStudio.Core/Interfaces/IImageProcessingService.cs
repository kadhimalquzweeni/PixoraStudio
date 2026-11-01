
using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Core.Interfaces;

public interface IImageProcessingService
{
    SKBitmap LoadImage(string filePath);

    SKBitmap ApplyAdjustments(
        SKBitmap original,
        ImageAdjustment adjustment);

    SKBitmap CloneImage(SKBitmap source);
}
