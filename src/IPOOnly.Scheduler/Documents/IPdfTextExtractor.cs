namespace IPOOnly.Scheduler.Documents;

public interface IPdfTextExtractor
{
    IReadOnlyList<PdfPageText> ExtractPages(byte[] pdfBytes);
}
