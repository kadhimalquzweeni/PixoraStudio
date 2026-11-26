using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Rendering.Skia;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Adapts the PdfPig/Skia renderer to PixoraStudio's existing SKBitmap image pipeline.
/// Each operation owns its PDF document so callers can safely render pages independently.
/// </summary>
public sealed class PdfRenderingService : IPdfRenderingService
{
    public PdfDocumentInfo GetDocumentInfo(string filePath)
    {
        ValidateFilePath(filePath);

        using var document = PdfDocument.Open(filePath, SkiaRenderingParsingOptions.Instance);
        var pages = new List<PdfPageInfo>(document.NumberOfPages);

        for (int pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            var page = document.GetPage(pageNumber);
            pages.Add(new PdfPageInfo(pageNumber, page.Width, page.Height));
        }

        return new PdfDocumentInfo(Path.GetFullPath(filePath), document.NumberOfPages, pages);
    }

    public SKBitmap RenderPage(string filePath, int pageNumber, int dpi)
    {
        ValidateFilePath(filePath);

        if (dpi is < 72 or > 600)
            throw new ArgumentOutOfRangeException(nameof(dpi), "PDF render resolution must be between 72 and 600 DPI.");

        using var document = PdfDocument.Open(filePath, SkiaRenderingParsingOptions.Instance);

        if (pageNumber < 1 || pageNumber > document.NumberOfPages)
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Page number must be between 1 and {document.NumberOfPages}.");

        document.AddSkiaPageFactory();
        float scale = dpi / 72f;

        return document.GetPageAsSKBitmap(pageNumber, scale, RGBColor.White);
    }

    private static void ValidateFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("The selected PDF file could not be found.", filePath);

        if (!string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The selected file must be a PDF document.", nameof(filePath));
    }
}
