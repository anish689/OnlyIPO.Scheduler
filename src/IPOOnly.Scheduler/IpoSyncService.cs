using IPOOnly.Scheduler.Persistence;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

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
                await repository.UpsertSourceSnapshotAsync(
                    SourceSnapshot(
                        ipoId: null,
                        sourceRecordId: $"{status}:page:{pageNumber}",
                        sourceEndpoint: page.SourceEndpoint,
                        rawJson: page.RawJson,
                        fetchedAtUtc: fetchedAt),
                    cancellationToken);

                var summaries = page.Payload.Data ?? [];

                foreach (var summary in summaries.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
                {
                    var detail = await client.GetIpoDetailAsync(summary.Id, cancellationToken);
                    var record = mapper.Map(summary, detail.Payload.Data, fetchedAt);

                    var ipoId = await repository.UpsertAsync(record, cancellationToken);
                    await repository.UpsertSourceSnapshotAsync(
                        SourceSnapshot(
                            ipoId,
                            summary.Id,
                            detail.SourceEndpoint,
                            detail.RawJson,
                            fetchedAt),
                        cancellationToken);
                    await repository.ReplaceTimelineEventsAsync(ipoId, mapper.MapTimeline(record, fetchedAt), cancellationToken);
                    await repository.ReplaceDocumentsAsync(ipoId, mapper.MapDocuments(record, fetchedAt), cancellationToken);
                    await repository.InsertSubscriptionSnapshotsAsync(
                        ipoId,
                        mapper.MapSubscriptionSnapshots(record, fetchedAt),
                        cancellationToken);
                    fetched++;
                    upserted++;
                }

                hasNextPage = page.Payload.MetaData?.Page is { } metadata
                    && pageNumber < metadata.TotalPages
                    && summaries.Count > 0;
                pageNumber++;
            }
        }

        logger.LogInformation("IPO sync complete. Fetched {FetchedCount}, upserted {UpsertedCount}.", fetched, upserted);
        return new IpoSyncResult(fetched, upserted);
    }

    private static IpoSourceSnapshotRecord SourceSnapshot(
        Guid? ipoId,
        string sourceRecordId,
        string sourceEndpoint,
        string rawJson,
        DateTimeOffset fetchedAtUtc)
    {
        return new IpoSourceSnapshotRecord(
            ipoId,
            "Upstox",
            sourceRecordId,
            sourceEndpoint,
            "Fetched",
            rawJson,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson))),
            fetchedAtUtc,
            fetchedAtUtc);
    }
}

public sealed record IpoSyncResult(int FetchedCount, int UpsertedCount);
