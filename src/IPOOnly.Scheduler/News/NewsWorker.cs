using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPOOnly.Scheduler.News;

public sealed class NewsWorker(INewsStore store, INewsFeed feed, ILogger<NewsWorker> logger) : BackgroundService
{
    public async Task<int> RunOnceAsync(CancellationToken token)
    {
        var owner = Guid.NewGuid();
        var sources = await store.ClaimAsync(owner, token);
        var failures = 0;
        foreach (var source in sources)
        {
            NewsBatch? batch = null;
            var outcome = "Failed";
            try { batch = await feed.ReadAsync(source, token); outcome = batch.NotModified ? "NotModified" : "Fetched"; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is HttpRequestException or InvalidDataException or System.Xml.XmlException or FormatException or OperationCanceledException)
            { failures++; outcome = error is OperationCanceledException ? "Timeout" : "RejectedFeed"; }
            await store.CompleteAsync(source, owner, batch, outcome, token);
            logger.LogInformation("News source {SourceId}: {Outcome}; {Count} entries staged for review.", source.Id, outcome, batch?.Entries.Count ?? 0);
        }
        logger.LogInformation("News check complete: {Count} approved due sources, {Failures} failures.", sources.Count, failures);
        return failures;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("News check failed; other scheduler jobs are unaffected."); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
