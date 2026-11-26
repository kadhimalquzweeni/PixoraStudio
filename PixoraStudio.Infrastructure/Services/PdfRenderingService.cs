using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Rendering.Skia;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Adapts the PdfPig/Skia renderer to PixoraStudio's existing SKBitmap image pipeline.
/// Batch rendering keeps one PDF document open for the duration of an export.
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
        ValidateDpi(dpi);

        using var document = PdfDocument.Open(filePath, SkiaRenderingParsingOptions.Instance);
        ValidatePageNumber(pageNumber, document.NumberOfPages);
        document.AddSkiaPageFactory();

        return document.GetPageAsSKBitmap(pageNumber, dpi / 72f, RGBColor.White);
    }

    public void RenderPages(
        string filePath,
        IReadOnlyCollection<int> pageNumbers,
        int dpi,
        Action<int, SKBitmap> pageRendered,
        CancellationToken cancellationToken = default)
    {
        ValidateFilePath(filePath);
        ArgumentNullException.ThrowIfNull(pageNumbers);
        ArgumentNullException.ThrowIfNull(pageRendered);
        ValidateDpi(dpi);

        int[] requestedPages = pageNumbers.Distinct().ToArray();
        if (requestedPages.Length == 0)
            throw new ArgumentException("Select at least one PDF page to render.", nameof(pageNumbers));

        using var document = PdfDocument.Open(filePath, SkiaRenderingParsingOptions.Instance);
        foreach (int pageNumber in requestedPages)
            ValidatePageNumber(pageNumber, document.NumberOfPages);

        document.AddSkiaPageFactory();
        float scale = dpi / 72f;

        foreach (int pageNumber in requestedPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var bitmap = document.GetPageAsSKBitmap(pageNumber, scale, RGBColor.White);
            pageRendered(pageNumber, bitmap);
        }
    }

    private static void ValidatePageNumber(int pageNumber, int pageCount)
    {
        if (pageNumber < 1 || pageNumber > pageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Page number must be between 1 and {pageCount}.");
    }

    private static void ValidateDpi(int dpi)
    {
        if (dpi is < 72 or > 600)
            throw new ArgumentOutOfRangeException(nameof(dpi), "PDF render resolution must be between 72 and 600 DPI.");
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
