using IPOOnly.Scheduler.Documents;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using System.Net;
using System.Security.Cryptography;

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
        using var primary = await documents.GetAsync(rhpUrl, HttpCompletionOption.ResponseHeadersRead, token);
        var replacement = primary.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            ? configuration.GetSection("Financials:RhpReplacements").GetChildren()
                .SingleOrDefault(x => string.Equals(x["SourceUrl"], rhpUrl, StringComparison.Ordinal))
            : null;
        var replacementUrl = replacement?["Url"];
        var expectedHash = replacement?["Sha256"];
        if (replacement is not null && (!AllowedRhp(replacementUrl) || expectedHash?.Length != 64
            || !expectedHash.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Invalid reviewed RHP replacement configuration.");
        // Official PDFs can be large and slower from hosted runners. Keep this
        // longer bounded window exclusive to checksum-reviewed replacements.
        if (replacement is not null) deadline.CancelAfter(TimeSpan.FromMinutes(3));
        using var alternate = replacement is null ? null
            : await documents.GetAsync(replacementUrl, HttpCompletionOption.ResponseHeadersRead, token);
        var response = alternate ?? primary;
        if (!response.IsSuccessStatusCode) throw new FinancialSourceException("RHP", response.StatusCode);
        var sourceUrl = alternate is null ? rhpUrl! : replacementUrl!;
        using var buffer = await DownloadAsync(response, sourceUrl, token);
        if (alternate is not null)
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Reviewed RHP replacement checksum mismatch.");
            logger.LogInformation("Using checksum-verified official RHP replacement after provider HTTP {Status}.", (int)primary.StatusCode);
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
        return parser.Parse(pages, sourceUrl, DateTimeOffset.UtcNow);
    }

    private async Task<MemoryStream> DownloadAsync(HttpResponseMessage initial, string url, CancellationToken token)
    {
        const int maxBytes = 30 * 1024 * 1024;
        for (var attempt = 0; ; attempt++)
        {
            using var retry = attempt == 0 ? null
                : await documents.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            var response = retry ?? initial;
            if (!response.IsSuccessStatusCode) throw new FinancialSourceException("RHP", response.StatusCode);
            if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("RHP exceeds size limit.");
            var buffer = new MemoryStream();
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                var chunk = new byte[16384];
                int count;
                while ((count = await stream.ReadAsync(chunk, token)) > 0)
                {
                    if (buffer.Length + count > maxBytes) throw new InvalidDataException("RHP exceeds size limit.");
                    buffer.Write(chunk, 0, count);
                }
                return buffer;
            }
            catch (HttpIOException) when (attempt == 0 && !token.IsCancellationRequested)
            {
                buffer.Dispose();
                initial.Dispose();
                logger.LogWarning("RHP transfer interrupted; retrying once within the existing download deadline.");
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }
    }

    public static bool AllowedRhp(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && new[] { "assets.upstox.com", "www.sebi.gov.in", "www.nseindia.com", "nsearchives.nseindia.com", "www.bseindia.com", "www.bsesme.com" }.Contains(uri.Host);
}
