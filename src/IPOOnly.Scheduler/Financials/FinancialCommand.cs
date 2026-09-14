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
        await using var query = db.CreateCommand("""
            SELECT i."Id", s."SourceRecordId"
            FROM ipos i JOIN "IpoSourceSnapshots" s ON s."IpoId" = i."Id"
            WHERE i."Slug" = @slug AND s."SourceName" = 'Upstox'
            ORDER BY s."CapturedAtUtc" DESC LIMIT 1
            """);
        query.Parameters.AddWithValue("slug", slug);
        Guid id;
        string sourceId;
        await using (var reader = await query.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidDataException("No Upstox identity stored for this IPO.");
            id = reader.GetGuid(0);
            sourceId = reader.GetString(1);
        }
        var detail = (await ipos.GetIpoDetailAsync(sourceId, token)).Payload.Data;
        if (detail is null || detail.Id != sourceId) throw new InvalidDataException("IPO identity mismatch.");
        var rows = await service.FetchAsync(detail.Isin, detail.RhpUrl, token);
        if (!preview) await store.SaveAsync(id, rows, token);
        Console.WriteLine($"{slug}: {rows.Count} validated observations. {(preview ? "Preview only; database unchanged." : "Empty coverage retains last-known-good data.")}");
        foreach (var row in rows.OrderBy(x => x.PeriodEnd).ThenBy(x => x.Metric))
            Console.WriteLine($"{row.Source} | {row.Basis} | {row.PeriodEnd:yyyy-MM-dd} | {row.Metric} | {row.Value} {row.Currency} {row.Unit}");
    }
}
