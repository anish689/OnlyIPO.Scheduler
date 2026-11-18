using System.Net;
using IPOOnly.Scheduler.Gmp;

namespace IPOOnly.Scheduler.Tests;

public sealed class GmpFeedTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-18T10:00:00Z");
    private const string Json = """
        {"updated_at":"2026-11-18T09:00:00Z","source":"https://gmptoday.in","license":"CC-BY-4.0","sample":true,
        "ipos":[{"slug":"example","url":"https://gmptoday.in/ipo/example/","status":"Open","open_date":"2026-11-18",
        "price_band":100,"gmp_median":20,"gmp_min":18,"gmp_max":22,"gmp_sources":4}]}
        """;

    [Fact]
    public void Reads_licensed_sample_without_prediction_fields()
    {
        var feed = GmpTodayFeed.Parse(Json, Now);
        Assert.True(GmpTodayFeed.IsUsable(Assert.Single(feed.Ipos), feed, new(2026, 11, 18), 100, Now));
    }

    [Theory]
    [InlineData("CC-BY-4.0", "evaluation-only")]
    [InlineData("https://gmptoday.in\"", "https://other.example\"")]
    [InlineData("2026-11-18T09:00:00Z", "2026-11-17T09:00:00Z")]
    [InlineData("2026-11-18T09:00:00Z", "2026-11-18T11:00:00Z")]
    [InlineData("\"sample\":true", "\"sample\":false")]
    public void Rejects_contract_license_and_freshness_changes(string before, string after) =>
        Assert.Throws<InvalidDataException>(() => GmpTodayFeed.Parse(Json.Replace(before, after), Now));

    [Theory]
    [InlineData(0)] [InlineData(-5)]
    public void Real_zero_and_negative_quotes_are_preserved(decimal premium)
    {
        var feed = GmpTodayFeed.Parse(Json, Now);
        var entry = feed.Ipos[0] with { GmpMedian = premium, GmpMin = premium, GmpMax = premium };
        Assert.True(GmpTodayFeed.IsUsable(entry, feed, new(2026, 11, 18), 100, Now));
    }

    [Theory]
    [InlineData("missing")] [InlineData("future_open")] [InlineData("disputed")] [InlineData("single_source")]
    [InlineData("wrong_price")] [InlineData("unsafe_url")] [InlineData("wrong_issue_url")]
    public void Rejects_unusable_or_mismatched_entries(string reason)
    {
        var feed = GmpTodayFeed.Parse(Json, Now);
        var entry = feed.Ipos[0];
        entry = reason switch
        {
            "missing" => entry with { GmpMedian = null },
            "future_open" => entry with { OpenDate = new(2026, 11, 19) },
            "disputed" => entry with { GmpMax = 40 },
            "single_source" => entry with { GmpSources = 1 },
            "wrong_price" => entry with { PriceBand = 101 },
            "unsafe_url" => entry with { Url = "http://gmptoday.in/ipo/example/" },
            _ => entry with { Url = "https://gmptoday.in/ipo/something-else/" }
        };
        Assert.False(GmpTodayFeed.IsUsable(entry, feed, new(2026, 11, 18), 100, Now));
    }

    [Fact]
    public void Does_not_present_a_preopening_report_after_opening()
    {
        var feed = GmpTodayFeed.Parse(Json.Replace("2026-11-18T09:00:00Z", "2026-11-17T17:00:00Z"), Now);
        Assert.False(GmpTodayFeed.IsUsable(feed.Ipos[0], feed, new(2026, 11, 18), 100, Now));
    }

    [Fact]
    public void Rejects_extreme_numeric_input_without_overflow()
    {
        var feed = GmpTodayFeed.Parse(Json, Now);
        var entry = feed.Ipos[0] with { GmpMedian = decimal.MinValue, GmpMin = decimal.MinValue };
        Assert.False(GmpTodayFeed.IsUsable(entry, feed, new(2026, 11, 18), 100, Now));
    }

    [Fact]
    public async Task Rate_limit_failure_is_one_request_not_a_retry_loop()
    {
        var handler = new RateLimitedHandler();
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => GmpTodayFeed.FetchAsync(client, Now, default));
        Assert.Equal(1, handler.Calls);
    }

    private sealed class RateLimitedHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(GmpTodayFeed.Endpoint, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        }
    }
}
