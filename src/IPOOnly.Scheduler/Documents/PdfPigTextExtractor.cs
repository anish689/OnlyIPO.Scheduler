using UglyToad.PdfPig;

namespace IPOOnly.Scheduler.Documents;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public IReadOnlyList<PdfPageText> ExtractPages(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfDocument.Open(stream);

        return document.GetPages()
            .Select(page => new PdfPageText(page.Number, page.Text))
            .ToList();
    }
}
