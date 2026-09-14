using System.Net;
using System.Text;
using System.Text.Json;
using IPOOnly.Scheduler.News;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IPOOnly.Scheduler.Tests;

public sealed class UpstoxNewsTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 1, 0, 0, DateTimeKind.Utc);
    private static readonly NewsSource Source = new(Guid.Empty, UpstoxNewsFeed.Endpoint, "upstox.com", false, null, null, 7);
    private const string Key = "NSE_EQ|TEST";
    private static byte[] Payload(string key = Key, string url = "https://upstox.com/news/test", string headline = "Company update", long? time = null, int pages = 1) =>
        JsonSerializer.SerializeToUtf8Bytes(new { status = "success", data = new Dictionary<string, object[]> {
            [key] = [new { heading = headline, summary = "Allowed only with permission", article_link = url,
                published_time = time ?? new DateTimeOffset(Now.AddHours(-1)).ToUnixTimeMilliseconds() }] },
            metadata = new { page = new { page_number = 1, total_pages = pages } } });

    [Fact]
    public void Maps_milliseconds_and_omits_unlicensed_description()
    {
        var result = UpstoxNewsFeed.Parse(Payload(), Source, [Key], Now, 1);
        var entry = Assert.Single(result.Batch.Entries);
        Assert.Equal(Now.AddHours(-1), entry.PublishedAtUtc);
        Assert.Null(entry.Description);
        Assert.Equal(entry.ExternalIdHash, entry.UrlHash);
        Assert.NotNull(Assert.Single(UpstoxNewsFeed.Parse(Payload(), Source with { DescriptionAllowed = true }, [Key], Now, 1).Batch.Entries).Description);
    }

    [Theory]
    [InlineData("https://evil.com/a")]
    [InlineData("http://upstox.com/a")]
    [InlineData("https://user:pass@upstox.com/a")]
    public void Rejects_unapproved_links(string url) => Assert.Empty(UpstoxNewsFeed.Parse(Payload(url: url), Source, [Key], Now, 1).Batch.Entries);

    [Fact]
    public void Rejects_wrong_keys_pagination_and_malformed_envelope()
    {
        Assert.Throws<InvalidDataException>(() => UpstoxNewsFeed.Parse(Payload(key: "OTHER"), Source, [Key], Now, 1));
        Assert.Throws<InvalidDataException>(() => UpstoxNewsFeed.Parse(Payload(), Source, [Key], Now, 2));
        Assert.Throws<InvalidDataException>(() => UpstoxNewsFeed.Parse(Encoding.UTF8.GetBytes("{}"), Source, [Key], Now, 1));
        Assert.Throws<InvalidDataException>(() => UpstoxNewsFeed.Parse(new byte[NewsFeed.MaxBytes + 1], Source, [Key], Now, 1));
    }

    [Fact]
    public void Rejects_markup_and_dates_outside_seven_days()
    {
        Assert.Empty(UpstoxNewsFeed.Parse(Payload(headline: "<b>IPO</b>"), Source, [Key], Now, 1).Batch.Entries);
        foreach (var date in new[] { Now.AddDays(1), Now.AddDays(-8) })
            Assert.Empty(UpstoxNewsFeed.Parse(Payload(time: new DateTimeOffset(date).ToUnixTimeMilliseconds()), Source, [Key], Now, 1).Batch.Entries);
    }

    private sealed class Instruments(int count) : INewsInstruments
    {
        public Task<IReadOnlyList<string>> ReadAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>(Enumerable.Range(0, count).Select(i => "NSE_EQ|" + i).ToArray());
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(respond(request)); }
    }
    private static UpstoxNewsFeed Client(Handler handler, int count = 1) => new(new HttpClient(handler), new Instruments(count),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Upstox:AnalyticsToken"] = "test-secret" }).Build());

    [Fact]
    public async Task Batches_thirty_keys_and_uses_fixed_authorized_origin()
    {
        var handler = new Handler(request => {
            Assert.Equal("api.upstox.com", request.RequestUri!.Host);
            Assert.Equal("test-secret", request.Headers.Authorization!.Parameter);
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.Contains("category=instrument_keys", query);
            var keys = query.Split("instrument_keys=")[1].Split('&')[0].Split(',');
            Assert.InRange(keys.Length, 1, 30);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"success\",\"data\":{},\"metadata\":{\"page\":{\"page_number\":1,\"total_pages\":0}}}") };
        });
        Assert.Empty((await Client(handler, 31).ReadAsync(Source, default)).Entries);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task Fails_closed_without_retrying_auth_quota_or_redirect(HttpStatusCode status)
    {
        var handler = new Handler(_ => new(status));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).ReadAsync(Source, default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Rejects_source_and_instrument_overflow_before_network()
    {
        var handler = new Handler(_ => throw new Exception("Must not call"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(handler).ReadAsync(Source with { FeedUrl = "https://evil.com" }, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(handler, 301).ReadAsync(Source, default));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Follows_pages_and_rejects_run_budget_overflow()
    {
        var handler = new Handler(request => {
            var page = int.Parse(request.RequestUri!.Query.Split("page_number=")[1]);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                status = "success", data = new { }, metadata = new { page = new { page_number = page, total_pages = 100 } }
            })) };
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(handler).ReadAsync(Source, default));
        Assert.Equal(30, handler.Calls);
    }

    [Fact]
    public async Task Deduplicates_same_article_across_instrument_batches()
    {
        var published = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        var handler = new Handler(request => {
            var key = Uri.UnescapeDataString(request.RequestUri!.Query).Split("instrument_keys=")[1].Split('&')[0].Split(',')[0];
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload(key: key, time: published)) };
        });
        Assert.Single((await Client(handler, 31).ReadAsync(Source, default)).Entries);
        Assert.Equal(2, handler.Calls);
    }
}
