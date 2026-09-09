using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Options;

namespace IPOOnly.Scheduler.Tracking;

public interface IMarketDataClient
{
    Task<IReadOnlyList<EquityInstrument>> GetInstrumentsAsync(string identity, CancellationToken token);
    Task<CandleBatch> GetCandlesAsync(string key, DateOnly from, DateOnly to, CancellationToken token);
}

public sealed class UpstoxMarketClient(HttpClient http, IOptions<UpstoxOptions> options) : IMarketDataClient
{
    public async Task<IReadOnlyList<EquityInstrument>> GetInstrumentsAsync(string identity, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(identity) || identity.Length > 50) return [];
        var instruments = new List<EquityInstrument>();
        for (var page = 1; page <= 10; page++)
        {
            using var response = await SendAsync($"https://api.upstox.com/v2/instruments/search?query={Uri.EscapeDataString(identity)}&segments=EQ&exchanges=NSE,BSE&records=30&page_number={page}", true, token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (document.RootElement.GetProperty("status").GetString() != "success") throw new InvalidDataException("Instrument lookup failed.");
            var rows = document.RootElement.GetProperty("data").Deserialize<EquityInstrument[]>() ?? [];
            instruments.AddRange(rows);
            var totalPages = document.RootElement.TryGetProperty("meta_data", out var metadata) && metadata.TryGetProperty("page", out var pages)
                ? pages.GetProperty("total_pages").GetInt32() : (rows.Length < 30 ? page : page + 1);
            if (page >= totalPages) return instruments;
        }
        throw new InvalidDataException("Instrument lookup exceeded the page limit; no partial match is accepted.");
    }

    public async Task<CandleBatch> GetCandlesAsync(string key, DateOnly from, DateOnly to, CancellationToken token)
    {
        var endpoint = $"https://api.upstox.com/v3/historical-candle/{Uri.EscapeDataString(key)}/days/1/{to:yyyy-MM-dd}/{from:yyyy-MM-dd}";
        using var response = await SendAsync(endpoint, true, token);
        var raw = await response.Content.ReadAsStringAsync(token);
        return new(endpoint, raw, MarketDataParser.Parse(raw, from, to));
    }

    private async Task<HttpResponseMessage> SendAsync(string url, bool authenticated, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("OnlyIPO.Scheduler/1.0");
            request.Headers.Accept.ParseAdd("application/json");
            if (authenticated)
            {
                var secret = string.IsNullOrWhiteSpace(options.Value.MarketDataToken) ? options.Value.AnalyticsToken : options.Value.MarketDataToken;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            }
            var response = await http.SendAsync(request, token);
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode;
            var retryDelay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * (attempt + 1));
            response.Dispose();
            if (attempt >= 2 || (status != HttpStatusCode.TooManyRequests && (int)status < 500))
                throw new HttpRequestException($"Upstox market-data request failed with HTTP {(int)status}.", null, status);
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(retryDelay.TotalSeconds, 1, 60)), token);
        }
    }
}
