using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace IPOOnly.Scheduler.Financials;

public static class FinancialCommand
{
    // Explicit single-company refresh keeps local validation separate from a full IPO sync.
    public static async Task RunAsync(NpgsqlDataSource db, IUpstoxIpoClient ipos, FinancialEnrichmentService service,
        FinancialStore store, IConfiguration configuration, bool preview, CancellationToken token)
    {
        var slug = configuration["Financials:Slug"];
        if (string.IsNullOrWhiteSpace(slug)) throw new InvalidOperationException("Financials:Slug is required.");
        if (!preview && !configuration.GetValue<bool>("Financials:Enabled"))
            throw new InvalidOperationException("Enable Financials:Enabled after applying the database migration.");
        var target = (await FinancialBatch.TargetsAsync(db, token, slug)).SingleOrDefault();
        if (target?.SourceId is not { } sourceId) throw new InvalidDataException("No Upstox IPO identity stored for this company.");
        var id = target.Id;
        var detail = (await ipos.GetIpoDetailAsync(sourceId, token)).Payload.Data;
        if (detail is null || detail.Id != sourceId) throw new InvalidDataException("IPO identity mismatch.");
        Console.WriteLine($"Financial source identity: {detail.Id}; ISIN {detail.Isin ?? "not supplied"}.");
        var rows = await service.FetchAsync(detail.Isin, detail.RhpUrl, token);
        if (!preview) await store.SaveAsync(id, rows, token);
        Console.WriteLine($"{slug}: {rows.Count} validated observations. {(preview ? "Preview only; database unchanged." : "Empty coverage retains last-known-good data.")}");
        foreach (var row in rows.OrderBy(x => x.PeriodEnd).ThenBy(x => x.Metric))
            Console.WriteLine($"{row.Source} | {row.Basis} | {row.PeriodEnd:yyyy-MM-dd} | {row.Metric} | {row.Value} {row.Currency} {row.Unit}");
    }
}
