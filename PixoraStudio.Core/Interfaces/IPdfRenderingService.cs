using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Core.Interfaces;

/// <summary>Loads PDF metadata and renders individual pages into PixoraStudio image surfaces.</summary>
public interface IPdfRenderingService
{
    PdfDocumentInfo GetDocumentInfo(string filePath);

    /// <summary>Renders a 1-based PDF page at the requested DPI onto a white background.</summary>
    SKBitmap RenderPage(string filePath, int pageNumber, int dpi);
}
