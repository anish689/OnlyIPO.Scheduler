using System.Net;
using IPOOnly.Scheduler.Tracking;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IPOOnly.Scheduler.Tests;

public sealed class MarketDataTests
{
    private static readonly DateOnly Date = new(2026, 9, 1);
    private static readonly EquityInstrument Instrument = new("NSE", "NSE_EQ", "TEST", "INE000000001", "NSE_EQ|INE000000001");
    private static TrackingCandidate Candidate => new(Guid.NewGuid(), "test-ipo", Date, 3, "INE000000001", "TEST", null, null, null);

    [Fact]
    public void Exact_isin_selects_nse_and_rejects_symbol_fallback_on_conflict()
    {
        Assert.Equal(Instrument, MarketDataParser.Match(Candidate, [Instrument, Instrument with { Exchange = "BSE", Segment = "BSE_EQ", Key = "BSE_EQ|INE000000001" }]));
        Assert.Null(MarketDataParser.Match(Candidate with { Isin = "INE000000002" }, [Instrument]));
        Assert.Equal(Instrument, MarketDataParser.Match(Candidate with { Isin = null }, [Instrument]));
        Assert.Null(MarketDataParser.Match(Candidate with { Isin = null, Symbol = "Company name" }, [Instrument]));
    }

    [Fact]
    public void Ambiguous_symbol_wrong_exchange_and_derivatives_are_rejected()
    {
        Assert.Null(MarketDataParser.Match(Candidate with { Isin = null }, [Instrument, Instrument with { Isin = "INE000000002", Key = "NSE_EQ|INE000000002" }]));
        Assert.Null(MarketDataParser.Match(Candidate with { Exchanges = 2 }, [Instrument]));
        Assert.Null(MarketDataParser.Match(Candidate, [Instrument with { Segment = "NSE_FO" }]));
    }

    [Fact]
    public void Candles_preserve_ohlcv_and_dates()
    {
        var row = Assert.Single(MarketDataParser.Parse(Payload("[\"2026-09-01T00:00:00+05:30\",100,120,90,110,12345,0]"), Date, Date));
        Assert.Equal(100, row.Open);
        Assert.Equal(110, row.Close);
        Assert.Equal(12345, row.Volume);
        Assert.Equal(Date, row.Date);
    }

    [Theory]
    [InlineData("[\"2026-09-01T00:00:00+05:30\",100,99,90,110,1,0]")]
    [InlineData("[\"2026-09-01T00:00:00+05:30\",100,120,90,110,-1,0]")]
    [InlineData("[\"2026-09-02T00:00:00+05:30\",100,120,90,110,1,0]")]
    [InlineData("[\"2026-09-01T00:00:00Z\",100,120,90,110,1,0]")]
    public void Invalid_candles_are_rejected(string row) => Assert.Throws<InvalidDataException>(() => MarketDataParser.Parse(Payload(row), Date, Date));

    [Fact]
    public void Duplicate_dates_are_rejected()
    {
        const string row = "[\"2026-09-01T00:00:00+05:30\",100,120,90,110,1,0]";
        Assert.Throws<InvalidDataException>(() => MarketDataParser.Parse(Payload(row + "," + row), Date, Date));
    }

    [Fact]
    public async Task Authentication_failure_is_not_retried_or_leaked()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var client = new UpstoxMarketClient(http, Options.Create(new UpstoxOptions { AnalyticsToken = "private-test-token" }));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCandlesAsync(Instrument.Key, Date, Date, default));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain("private-test-token", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task One_provider_failure_does_not_stop_other_ipos_or_save_failed_data()
    {
        var store = new MemoryStore();
        var client = new FakeClient();
        var service = new TrackingSyncService(client, store, Options.Create(new TrackingOptions()), NullLogger<TrackingSyncService>.Instance);
        var result = await service.SyncAsync(default);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, store.Saved);
    }

    private static string Payload(string rows) => "{\"status\":\"success\",\"data\":{\"candles\":[" + rows + "]}}";
    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }
    private sealed class MemoryStore : ITrackingStore
    {
        public int Saved { get; private set; }
        public Task<IReadOnlyList<TrackingCandidate>> GetCandidatesAsync(DateOnly today, int days, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<TrackingCandidate>>([Candidate with { ListingDate = today.AddDays(-1) }, Candidate with { ListingDate = today.AddDays(-1) }]);
        public Task<Guid> MapAsync(TrackingCandidate ipo, EquityInstrument instrument, CancellationToken token) => Task.FromResult(Guid.NewGuid());
        public Task SaveAsync(Guid ipo, Guid instrument, CandleBatch batch, CancellationToken token) { Saved++; return Task.CompletedTask; }
    }
    private sealed class FakeClient : IMarketDataClient
    {
        private int _calls;
        public Task<IReadOnlyList<EquityInstrument>> GetInstrumentsAsync(string identity, CancellationToken token) => Task.FromResult<IReadOnlyList<EquityInstrument>>([Instrument]);
        public Task<CandleBatch> GetCandlesAsync(string key, DateOnly from, DateOnly to, CancellationToken token)
        {
            if (_calls++ == 0) throw new HttpRequestException("Provider failure");
            return Task.FromResult(new CandleBatch("https://api.upstox.com/v3/historical-candle", Payload(""), []));
        }
    }
}
