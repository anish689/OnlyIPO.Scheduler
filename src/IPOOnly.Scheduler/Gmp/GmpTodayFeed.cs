using System.Text.Json;

namespace IPOOnly.Scheduler.Gmp;

public sealed record GmpFeed(DateTimeOffset UpdatedAt, string Source, string License, bool Sample, GmpEntry[] Ipos);
public sealed record GmpEntry(string Slug, string Url, string Status, DateOnly? OpenDate, decimal? PriceBand,
    decimal? GmpMedian, decimal? GmpMin, decimal? GmpMax, int? GmpSources);

public static class GmpTodayFeed
{
    public const string Endpoint = "https://gmptoday.in/api/gmp.json";
    public const string RightsReference = "https://gmptoday.in/api/ | CC-BY-4.0 | reviewed 2026-11-18";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    public static GmpFeed Parse(string json, DateTimeOffset now)
    {
        var feed = JsonSerializer.Deserialize<GmpFeed>(json, JsonOptions) ?? throw new InvalidDataException("Empty GMP feed.");
        if (feed.Source != "https://gmptoday.in" || feed.License != "CC-BY-4.0" || !feed.Sample
            || feed.UpdatedAt > now || feed.UpdatedAt <= now.AddHours(-24)
            || feed.Ipos is null || feed.Ipos.Length > 200
            || feed.Ipos.Any(x => x is null || string.IsNullOrWhiteSpace(x.Slug))
            || feed.Ipos.Select(x => x.Slug).Distinct(StringComparer.Ordinal).Count() != feed.Ipos.Length)
            throw new InvalidDataException("GMP feed contract, license or freshness changed.");
        return feed;
    }

    public static bool IsUsable(GmpEntry entry, GmpFeed feed, DateOnly openDate, decimal upperPrice, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, India).Date);
        var reportedDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(feed.UpdatedAt, India).Date);
        return entry.Status is "Open" or "Closed" && entry.OpenDate == openDate && openDate <= today
            && reportedDate >= openDate && entry.PriceBand == upperPrice && upperPrice > 0
            && entry.GmpMedian is { } median && entry.GmpMin is { } low && entry.GmpMax is { } high
            && median is >= -1_000_000 and <= 1_000_000
            && low is >= -1_000_000 and <= 1_000_000 && high is >= -1_000_000 and <= 1_000_000
            && low <= median && high >= median
            && high - low <= Math.Abs(median) * 0.5m && entry.GmpSources is >= 2 and <= 100
            && Uri.TryCreate(entry.Url, UriKind.Absolute, out var url) && url.Scheme == "https"
            && url.Host == "gmptoday.in" && url.IsDefaultPort && url.UserInfo == "" && url.Query == "" && url.Fragment == ""
            && url.AbsolutePath == $"/ipo/{entry.Slug}/";
    }

    public static async Task<GmpFeed> FetchAsync(HttpClient client, DateTimeOffset now, CancellationToken token)
    {
        // One bounded request per run; never call this from the API or browser.
        var json = await client.GetStringAsync(Endpoint, token);
        return Parse(json, now);
    }
}
