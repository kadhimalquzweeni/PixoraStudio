namespace PixoraStudio.Core.Models;

public readonly record struct PdfPixelDimensions(int Width, int Height);

/// <summary>
/// Calculates raster output dimensions using the same ceiling rule as the PDF renderer.
/// PDF page dimensions are expressed in points (1/72 inch).
/// </summary>
public static class PdfOutputDimensions
{
    public static PdfPixelDimensions FromPoints(double widthPoints, double heightPoints, int dpi)
    {
        if (!double.IsFinite(widthPoints) || widthPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthPoints));
        if (!double.IsFinite(heightPoints) || heightPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightPoints));
        if (dpi is < 72 or > 600)
            throw new ArgumentOutOfRangeException(nameof(dpi));

        double scale = dpi / 72d;
        return new PdfPixelDimensions(
            Math.Max(1, (int)Math.Ceiling(widthPoints * scale)),
            Math.Max(1, (int)Math.Ceiling(heightPoints * scale)));
    }
}
