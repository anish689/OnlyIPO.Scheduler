using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IPOOnly.Scheduler.Tracking;

public sealed class TrackingOptions
{
    public bool Enabled { get; init; }
    public int RecentDays { get; init; } = 90;
    public int RefreshHours { get; init; } = 24;
}

public sealed record TrackingSyncResult(int Eligible, int Updated, int Unmatched, int Failed);

public sealed class TrackingSyncService(IMarketDataClient client, ITrackingStore store,
    IOptions<TrackingOptions> options, ILogger<TrackingSyncService> logger)
{
    public async Task<TrackingSyncResult> SyncAsync(CancellationToken token)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var candidates = await store.GetCandidatesAsync(today, options.Value.RecentDays, token);
        var updated = 0;
        var unmatched = 0;
        var failed = 0;
        foreach (var ipo in candidates)
        {
            try
            {
                var master = await client.GetInstrumentsAsync(ipo.Isin ?? ipo.Symbol ?? "", token);
                var instrument = MarketDataParser.Match(ipo, master);
                if (instrument is null || (ipo.InstrumentKey is not null && ipo.InstrumentKey != instrument.Key))
                {
                    unmatched++;
                    logger.LogWarning("Tracking mapping unavailable or changed for {Slug}.", ipo.Slug);
                    continue;
                }
                var id = await store.MapAsync(ipo, instrument, token);
                var from = ipo.LatestDate?.AddDays(-5) ?? ipo.ListingDate;
                if (from < ipo.ListingDate) from = ipo.ListingDate;
                // Bound historical requests for older saved IPOs; fetch listing day separately.
                if (from < today.AddDays(-options.Value.RecentDays)) from = today.AddDays(-options.Value.RecentDays);
                var batch = await client.GetCandlesAsync(instrument.Key, from, today.AddDays(-1), token);
                await store.SaveAsync(ipo.IpoId, id, batch, token);
                if (ipo.ListingDate < from)
                {
                    var listing = await client.GetCandlesAsync(instrument.Key, ipo.ListingDate, ipo.ListingDate, token);
                    await store.SaveAsync(ipo.IpoId, id, listing, token);
                }
                updated++;
                await Task.Delay(300, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                failed++;
                logger.LogWarning("Tracking refresh failed for {Slug}: {ErrorType}, HTTP {Status}. Existing prices retained.",
                    ipo.Slug, exception.GetType().Name, (exception as HttpRequestException)?.StatusCode);
            }
        }
        logger.LogInformation("Tracking sync: eligible {Eligible}, updated {Updated}, unmatched {Unmatched}, failed {Failed}.", candidates.Count, updated, unmatched, failed);
        return new(candidates.Count, updated, unmatched, failed);
    }
}

public sealed class TrackingWorker(TrackingSyncService service, IOptions<TrackingOptions> options,
    NpgsqlDataSource source, ILogger<TrackingWorker> logger) : BackgroundService
{
    public async Task<TrackingSyncResult?> RunOnceAsync(CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(73193301)", connection);
        if (await command.ExecuteScalarAsync(token) is not true) return null;
        try { return await service.SyncAsync(token); }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(73193301)", connection);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Tracking job failed: {ErrorType}. Will retry at the next interval.", error.GetType().Name); }
            await Task.Delay(TimeSpan.FromHours(options.Value.RefreshHours), stoppingToken);
        }
    }
}
