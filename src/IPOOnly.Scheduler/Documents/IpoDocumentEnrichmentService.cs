using IPOOnly.Scheduler.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IPOOnly.Scheduler.Documents;

public sealed class IpoDocumentEnrichmentService(
    HttpClient httpClient,
    IpoOfferDocumentSelector selector,
    IPdfTextExtractor textExtractor,
    IpoOfferDocumentParser parser,
    IpoRepository repository,
    IOptions<DocumentEnrichmentOptions> options,
    ILogger<IpoDocumentEnrichmentService> logger)
{
    public async Task EnrichAsync(
        Guid ipoId,
        IReadOnlyList<IpoDocumentRecord> documents,
        DateTimeOffset fetchedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var preferred = selector.SelectPreferred(documents);
        if (preferred is null)
        {
            return;
        }

        try
        {
            var pdfBytes = await DownloadAsync(preferred.Url, cancellationToken);
            var pages = textExtractor.ExtractPages(pdfBytes);
            var facts = parser.Parse(preferred, pages, fetchedAtUtc);

            await repository.ReplaceDocumentFactsAsync(ipoId, preferred.DocumentType, facts, cancellationToken);
            logger.LogInformation(
                "Document enrichment extracted {FactCount} facts from {DocumentType} for IPO {IpoId}.",
                facts.Count,
                preferred.DocumentType,
                ipoId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Document enrichment skipped {DocumentType} for IPO {IpoId}. Existing IPO data remains available.",
                preferred.DocumentType,
                ipoId);
        }
    }

    private async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > options.Value.MaxDocumentBytes)
        {
            throw new InvalidDataException($"Document exceeds configured limit of {options.Value.MaxDocumentBytes} bytes.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > options.Value.MaxDocumentBytes)
        {
            throw new InvalidDataException($"Document exceeds configured limit of {options.Value.MaxDocumentBytes} bytes.");
        }

        if (!LooksLikePdf(bytes))
        {
            throw new InvalidDataException("Document response is not a PDF.");
        }

        return bytes;
    }

    private static bool LooksLikePdf(byte[] bytes)
    {
        return bytes.Length >= 5
            && bytes[0] == '%'
            && bytes[1] == 'P'
            && bytes[2] == 'D'
            && bytes[3] == 'F'
            && bytes[4] == '-';
    }
}
