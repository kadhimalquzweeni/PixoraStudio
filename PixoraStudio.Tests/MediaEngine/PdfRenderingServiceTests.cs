using System.Globalization;
using System.Text;
using PixoraStudio.Infrastructure.Services;
using SkiaSharp;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class PdfRenderingServiceTests
{
    private readonly PdfRenderingService _service = new();

    [Fact]
    public void GetDocumentInfo_ShouldReturnPageCountAndDimensions()
    {
        using var pdf = CreatePdf();
        string path = WriteTemporaryPdf(pdf);

        try
        {
            var info = _service.GetDocumentInfo(path);

            Assert.Equal(Path.GetFullPath(path), info.FilePath);
            Assert.Equal(1, info.PageCount);
            var page = Assert.Single(info.Pages);
            Assert.Equal(1, page.PageNumber);
            Assert.Equal(200, page.WidthPoints);
            Assert.Equal(100, page.HeightPoints);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RenderPage_ShouldReturnSkiaBitmapAtRequestedDpi()
    {
        using var pdf = CreatePdf();
        string path = WriteTemporaryPdf(pdf);

        try
        {
            using var bitmap = _service.RenderPage(path, 1, 72);

            Assert.Equal(200, bitmap.Width);
            Assert.Equal(100, bitmap.Height);
            Assert.NotEqual(SKColors.White, bitmap.GetPixel(100, 50));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RenderPages_ShouldRenderRequestedPagesInsideOneBatch()
    {
        using var pdf = CreatePdf();
        string path = WriteTemporaryPdf(pdf);

        try
        {
            int renderedPages = 0;
            _service.RenderPages(path, [1], 72, (pageNumber, bitmap) =>
            {
                Assert.Equal(1, pageNumber);
                Assert.Equal(200, bitmap.Width);
                Assert.Equal(100, bitmap.Height);
                renderedPages++;
            });

            Assert.Equal(1, renderedPages);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RenderPage_ShouldRejectPageNumbersOutsideDocument(int pageNumber)
    {
        using var pdf = CreatePdf();
        string path = WriteTemporaryPdf(pdf);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _service.RenderPage(path, pageNumber, 72));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(71)]
    [InlineData(601)]
    public void RenderPage_ShouldRejectUnsupportedDpi(int dpi)
    {
        using var pdf = CreatePdf();
        string path = WriteTemporaryPdf(pdf);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _service.RenderPage(path, 1, dpi));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetDocumentInfo_ShouldRejectMissingFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        Assert.Throws<FileNotFoundException>(() => _service.GetDocumentInfo(path));
    }

    [Fact]
    public void GetDocumentInfo_ShouldRejectNonPdfFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "not a PDF");

        try
        {
            Assert.Throws<ArgumentException>(() => _service.GetDocumentInfo(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MemoryStream CreatePdf()
    {
        const string content = "0.1 0.35 0.8 rg\n10 10 180 80 re f\n";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream"
        ];

        var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };

        foreach (string pdfObject in objects)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{offsets.Count} 0 obj\n{pdfObject}\nendobj\n");
        }

        long xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Length + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");

        foreach (long offset in offsets.Skip(1))
            WriteAscii(stream, $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");

        WriteAscii(stream,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");

        stream.Position = 0;
        return stream;
    }

    private static string WriteTemporaryPdf(MemoryStream pdf)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        using var output = File.Create(path);
        pdf.CopyTo(output);
        return path;
    }

    private static void WriteAscii(Stream stream, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
