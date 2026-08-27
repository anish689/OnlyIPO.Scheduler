using IPOOnly.Scheduler.Persistence;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IPOOnly.Scheduler;

public sealed class IpoSyncService(
    IUpstoxIpoClient client,
    UpstoxIpoMapper mapper,
    IpoRepository repository,
    IOptions<SchedulerOptions> options,
    ILogger<IpoSyncService> logger)
{
    public async Task<IpoSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        var fetched = 0;
        var upserted = 0;

        foreach (var status in options.Value.Statuses.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pageNumber = 1;
            var hasNextPage = true;

            while (hasNextPage)
            {
                var page = await client.GetIpoPageAsync(status, pageNumber, options.Value.PageSize, cancellationToken);
                var summaries = page.Data ?? [];

                foreach (var summary in summaries.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
                {
                    var detail = await client.GetIpoDetailAsync(summary.Id, cancellationToken);
                    var record = mapper.Map(summary, detail.Data, fetchedAt);

                    await repository.UpsertAsync(record, cancellationToken);
                    fetched++;
                    upserted++;
                }

                hasNextPage = page.MetaData?.Page is { } metadata
                    && pageNumber < metadata.TotalPages
                    && summaries.Count > 0;
                pageNumber++;
            }
        }

        logger.LogInformation("IPO sync complete. Fetched {FetchedCount}, upserted {UpsertedCount}.", fetched, upserted);
        return new IpoSyncResult(fetched, upserted);
    }
}

public sealed record IpoSyncResult(int FetchedCount, int UpsertedCount);
