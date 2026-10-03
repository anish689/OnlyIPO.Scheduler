using System.Net;
using IPOOnly.Scheduler.Financials;

namespace IPOOnly.Scheduler.Tests;

public sealed class FinancialBatchTests
{
    private static FinancialTarget Target(string slug) => new(Guid.NewGuid(), slug, slug);

    [Fact]
    public async Task Scheduled_refresh_skips_fresh_sets_but_keeps_missing_or_stale_companies()
    {
        var fresh = Target("fresh"); var stale = Target("stale"); var missing = Target("missing");
        var due = await FinancialBatch.DueAsync([fresh, stale, missing],
            (id, _) => Task.FromResult(id == fresh.Id), default);
        Assert.Equal(new[] { stale, missing }, due);
    }

    [Fact]
    public async Task Due_selection_honors_cancellation()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FinancialBatch.DueAsync([Target("first")],
            (_, _) => throw new InvalidOperationException(), cancel.Token));
    }

    [Fact]
    public async Task Never_sends_a_market_snapshot_identity_to_the_ipo_endpoint()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => FinancialBatch.RefreshAsync(
            Target("market:internal-instrument-id"), null!, null!, null!, default));
    }

    [Fact]
    public async Task Missing_source_identity_is_unavailable_without_a_provider_request()
    {
        Assert.Equal(0, await FinancialBatch.RefreshAsync(new(Guid.NewGuid(), "missing", null), null!, null!, null!, default));
    }

    [Fact]
    public async Task Visits_every_company_and_reports_failures_without_discarding_successes()
    {
        var visited = new List<string>();
        var messages = new List<string>();
        var result = await FinancialBatch.RunAsync([Target("first"), Target("broken"), Target("empty"), Target("last")],
            (target, _) => { visited.Add(target.Slug); return target.Slug switch {
                "broken" => throw new HttpRequestException("private request details", null, HttpStatusCode.ServiceUnavailable),
                "empty" => Task.FromResult(0), _ => Task.FromResult(12) }; }, messages.Add, TimeSpan.Zero, default);
        Assert.Equal(new FinancialBatchResult(4, 2, 1, 1), result);
        Assert.Equal(new[] { "first", "broken", "empty", "last" }, visited);
        Assert.Contains(messages, x => x.Contains("HTTP 503"));
        Assert.DoesNotContain(messages, x => x.Contains("private request details"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Stops_on_authentication_or_rate_limit(HttpStatusCode status)
    {
        var calls = 0;
        await Assert.ThrowsAsync<HttpRequestException>(() => FinancialBatch.RunAsync([Target("first"), Target("last")],
            (_, _) => { calls++; throw new HttpRequestException(null, null, status); }, _ => { }, TimeSpan.Zero, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Restricted_document_is_a_reported_company_failure_not_a_global_auth_failure()
    {
        var messages = new List<string>();
        var result = await FinancialBatch.RunAsync([Target("restricted"), Target("next")],
            (target, _) => target.Slug == "restricted"
                ? throw new FinancialSourceException("RHP", HttpStatusCode.Forbidden) : Task.FromResult(12),
            messages.Add, TimeSpan.Zero, default);
        Assert.Equal(new FinancialBatchResult(2, 1, 0, 1), result);
        Assert.Contains(messages, x => x.Contains("RHP HTTP 403"));
    }

    [Theory]
    [InlineData("Upstox income statement", HttpStatusCode.Forbidden)]
    [InlineData("RHP", HttpStatusCode.TooManyRequests)]
    public async Task Source_auth_and_all_rate_limits_still_stop_the_batch(string source, HttpStatusCode status)
    {
        var messages = new List<string>(); var calls = 0;
        await Assert.ThrowsAsync<FinancialSourceException>(() => FinancialBatch.RunAsync([Target("first"), Target("next")],
            (_, _) => { calls++; throw new FinancialSourceException(source, status); }, messages.Add, TimeSpan.Zero, default));
        Assert.Equal(1, calls);
        Assert.Contains(messages, x => x.Contains($"first: {source} returned {(int)status}"));
    }

    [Fact]
    public async Task Honors_cancellation_before_contacting_provider()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FinancialBatch.RunAsync([Target("first")],
            (_, _) => throw new InvalidOperationException(), _ => { }, TimeSpan.Zero, cancel.Token));
    }
}
