using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;
using UglyToad.PdfPig;
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
        SKBitmap? result = null;
        RenderPages(filePath, [pageNumber], dpi, (_, bitmap) => result = bitmap.Copy());
        return result ?? throw new InvalidOperationException("The PDF page could not be rendered.");
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
        SKMatrix scaleMatrix = SKMatrix.CreateScale(scale, scale);

        foreach (int pageNumber in requestedPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var picture = document.GetPageAsSKPicture(pageNumber, cancellationToken);
            var pageSize = document.GetPageSize(pageNumber);
            PdfPixelDimensions dimensions = PdfOutputDimensions.FromPoints(
                pageSize.Width, pageSize.Height, dpi);

            using var bitmap = new SKBitmap(dimensions.Width, dimensions.Height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawPicture(picture, in scaleMatrix);
            }

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
