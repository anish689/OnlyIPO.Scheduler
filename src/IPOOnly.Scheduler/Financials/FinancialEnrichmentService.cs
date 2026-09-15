using IPOOnly.Scheduler.Documents;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace IPOOnly.Scheduler.Financials;

public sealed class FinancialEnrichmentService(UpstoxFinancialClient upstox, HttpClient documents,
    RhpFinancialParser parser, FinancialStore store, IConfiguration configuration, ILogger<FinancialEnrichmentService> logger)
{
    public async Task EnrichAsync(Guid ipoId, UpstoxIpoSummary summary, UpstoxIpoDetail? detail, CancellationToken token)
    {
        if (!configuration.GetValue<bool>("Financials:Enabled")) return;
        try
        {
            if (await store.IsFreshAsync(ipoId, token)) return;
            var isin = detail?.Isin ?? summary.Isin;
            if (!string.IsNullOrWhiteSpace(detail?.Isin) && !string.IsNullOrWhiteSpace(summary.Isin) && detail.Isin != summary.Isin)
                throw new InvalidDataException("IPO identity mismatch.");
            var rows = await FetchAsync(isin, detail?.RhpUrl, token);
            await store.SaveAsync(ipoId, rows, token);
            logger.LogInformation("Financial enrichment: {Count} observations for {IpoId}.", rows.Count, ipoId);
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            // Do not log request headers, tokens or document contents.
            logger.LogWarning("Financial enrichment failed ({ErrorType}) for {IpoId}; last-known-good data retained.", error.GetType().Name, ipoId);
        }
    }

    public async Task<IReadOnlyList<FinancialObservation>> FetchAsync(string? isin, string? rhpUrl, CancellationToken token)
    {
        // Authentication/network/schema failures throw; only confirmed empty coverage invokes fallback.
        var rows = string.IsNullOrWhiteSpace(isin) ? [] : await upstox.FetchAsync(isin, token);
        if (FinancialValidation.Usable(rows)) return rows;
        if (!AllowedRhp(rhpUrl)) return [];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        token = deadline.Token;
        using var response = await documents.GetAsync(rhpUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        const int maxBytes = 30 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("RHP exceeds size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > maxBytes) throw new InvalidDataException("RHP exceeds size limit.");
            buffer.Write(chunk, 0, count);
        }
        buffer.Position = 0;
        using var pdf = PdfDocument.Open(buffer);
        var pages = new List<PdfPageText>();
        foreach (var page in pdf.GetPages())
        {
            token.ThrowIfCancellationRequested();
            // Reconstruct line positions; flattening page.Text destroys financial column alignment.
            var lines = page.GetWords().GroupBy(x => Math.Round(x.BoundingBox.Bottom / 3) * 3)
                .OrderByDescending(x => x.Key).Select(g => string.Join(" ", g.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text)));
            pages.Add(new(page.Number, string.Join("\n", lines)));
        }
        return parser.Parse(pages, rhpUrl!, DateTimeOffset.UtcNow);
    }

    public static bool AllowedRhp(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && new[] { "assets.upstox.com", "www.sebi.gov.in", "www.nseindia.com", "nsearchives.nseindia.com", "www.bseindia.com", "www.bsesme.com" }.Contains(uri.Host);
}
