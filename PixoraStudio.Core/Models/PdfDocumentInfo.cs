namespace PixoraStudio.Core.Models;

/// <summary>Basic metadata for a PDF document and its pages.</summary>
public sealed record PdfDocumentInfo(
    string FilePath,
    int PageCount,
    IReadOnlyList<PdfPageInfo> Pages);

/// <summary>Page dimensions are expressed in PDF points (1/72 inch).</summary>
public sealed record PdfPageInfo(
    int PageNumber,
    double WidthPoints,
    double HeightPoints);
