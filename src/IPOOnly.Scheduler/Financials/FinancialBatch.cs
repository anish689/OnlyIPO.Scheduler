using System.Net;
using IPOOnly.Scheduler.Upstox;
using Npgsql;

namespace IPOOnly.Scheduler.Financials;

public sealed record FinancialTarget(Guid Id, string Slug, string? SourceId);
public sealed record FinancialBatchResult(int Attempted, int Populated, int Unavailable, int Failed);

public static class FinancialBatch
{
    public static async Task<IReadOnlyList<FinancialTarget>> TargetsAsync(NpgsqlDataSource db, CancellationToken token, string? slug = null)
    {
        await using var query = db.CreateCommand("""
            SELECT i."Id", i."Slug", s."SourceRecordId"
            FROM ipos i LEFT JOIN LATERAL (
                SELECT "SourceRecordId" FROM "IpoSourceSnapshots"
                WHERE "IpoId" = i."Id" AND "SourceName" = 'Upstox'
                    AND ("SourceEndpoint" = 'ipos/' || "SourceRecordId" OR "SourceEndpoint" LIKE 'ipos?%')
                    AND "SourceRecordId" NOT LIKE 'market:%'
                ORDER BY "CapturedAtUtc" DESC LIMIT 1
            ) s ON true
            WHERE (@slug IS NULL OR i."Slug" = @slug)
            ORDER BY i."Slug"
            """);
        query.Parameters.AddWithValue("slug", NpgsqlTypes.NpgsqlDbType.Text, (object?)slug ?? DBNull.Value);
        var targets = new List<FinancialTarget>();
        await using var reader = await query.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) targets.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return targets;
    }

    public static async Task<int> RefreshAsync(FinancialTarget target, IUpstoxIpoClient ipos,
        FinancialEnrichmentService service, FinancialStore store, CancellationToken token)
    {
        if (target.SourceId is null) return 0;
        if (target.SourceId.StartsWith("market:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Market snapshots cannot identify an IPO detail record.");
        var detail = (await ipos.GetIpoDetailAsync(target.SourceId, token)).Payload.Data;
        if (detail is null || detail.Id != target.SourceId) throw new InvalidDataException("IPO identity mismatch.");
        var rows = await service.FetchAsync(detail.Isin, detail.RhpUrl, token);
        await store.SaveAsync(target.Id, rows, token);
        return rows.Count;
    }

    public static async Task<FinancialBatchResult> RunAsync(IReadOnlyList<FinancialTarget> targets,
        Func<FinancialTarget, CancellationToken, Task<int>> refresh, Action<string> report,
        TimeSpan interval, CancellationToken token)
    {
        var populated = 0; var unavailable = 0; var failed = 0; var attempted = 0;
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            if (attempted > 0) await Task.Delay(interval, token);
            attempted++;
            try
            {
                var count = await refresh(target, token);
                if (count > 0) populated++; else unavailable++;
                report($"{target.Slug}: {(count > 0 ? $"populated ({count} observations)" : "no validated coverage; existing values retained")}");
            }
            catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                // Stop immediately on authentication/rate limits instead of repeating across the catalogue.
                report($"Batch stopped: provider returned {(int)error.StatusCode.Value}; completed values retained.");
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            {
                failed++;
                report($"{target.Slug}: failed ({FailureCode(error)}); existing values retained");
            }
        }
        var result = new FinancialBatchResult(attempted, populated, unavailable, failed);
        report($"Financial batch: {attempted} attempted; {populated} populated; {unavailable} unavailable; {failed} failed.");
        return result;
    }

    public static string FailureCode(Exception error) => error is HttpRequestException { StatusCode: { } status }
        ? $"HTTP {(int)status}" : error.GetType().Name;
}
