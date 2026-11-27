using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Core.Interfaces;

/// <summary>Loads PDF metadata and renders individual pages into PixoraStudio image surfaces.</summary>
public interface IPdfRenderingService
{
    PdfDocumentInfo GetDocumentInfo(string filePath);

    /// <summary>Renders a 1-based PDF page at the requested DPI onto a white background.</summary>
    SKBitmap RenderPage(string filePath, int pageNumber, int dpi);

    /// <summary>
    /// Renders pages in one open document. The supplied bitmap is valid only for the duration
    /// of the callback and is disposed immediately afterwards.
    /// </summary>
    void RenderPages(
        string filePath,
        IReadOnlyCollection<int> pageNumbers,
        int dpi,
        Action<int, SKBitmap> pageRendered,
        CancellationToken cancellationToken = default);
}
