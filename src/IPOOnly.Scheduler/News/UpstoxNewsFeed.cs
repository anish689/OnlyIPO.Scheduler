using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace IPOOnly.Scheduler.News;

public interface INewsInstruments
{
    Task<IReadOnlyList<string>> ReadAsync(CancellationToken token);
}

public sealed class NewsInstruments(NpgsqlDataSource database) : INewsInstruments
{
    public async Task<IReadOnlyList<string>> ReadAsync(CancellationToken token)
    {
        await using var command = database.CreateCommand("""
            SELECT DISTINCT m."InstrumentKey" FROM "ListedInstruments" m
            JOIN ipos i ON i."Id"=m."IpoId"
            WHERE m."SourceName"='Upstox' AND i."Status"<>'Withdrawn'
            ORDER BY m."InstrumentKey" LIMIT 301
            """);
        await using var rows = await command.ExecuteReaderAsync(token);
        var keys = new List<string>();
        while (await rows.ReadAsync(token)) keys.Add(rows.GetString(0));
        return keys;
    }
}

// Keep credentials on the fixed API origin, never on a source-controlled URL.
public sealed class UpstoxNewsFeed(HttpClient client, INewsInstruments instruments, IConfiguration configuration)
{
    public const string Endpoint = "https://api.upstox.com/v2/news";

    public async Task<NewsBatch> ReadAsync(NewsSource source, CancellationToken token)
    {
        if (source.FeedUrl != Endpoint || source.ArticleHost != "upstox.com")
            throw new InvalidDataException("Invalid Upstox news source.");
        var credential = configuration["Upstox:NewsToken"];
        if (string.IsNullOrWhiteSpace(credential)) credential = configuration["Upstox:AnalyticsToken"];
        if (string.IsNullOrWhiteSpace(credential)) throw new InvalidDataException("Upstox news token is not configured.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var ct = deadline.Token;
        var keys = (await instruments.ReadAsync(ct)).Distinct().ToArray();
        if (keys.Length > 300) throw new InvalidDataException("News instrument capacity exceeded; partition the job before increasing coverage.");
        var results = new Dictionary<string, NewsEntry>(StringComparer.Ordinal);
        var rejected = 0;
        var requests = 0;
        foreach (var batch in keys.Chunk(30))
        {
            for (var page = 1; ; page++)
            {
                if (++requests > 30) throw new InvalidDataException("News pagination budget exceeded; no partial batch is saved.");
                var uri = Endpoint + "?category=instrument_keys&instrument_keys=" + Uri.EscapeDataString(string.Join(",", batch))
                    + "&page_size=100&page_number=" + page;
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                // Defer 429/5xx to the next scheduled attempt; do not spin on quota or auth failures.
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Upstox news HTTP {(int)response.StatusCode}.", null, response.StatusCode);
                if (response.Content.Headers.ContentLength > NewsFeed.MaxBytes) throw new InvalidDataException("News response too large.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream();
                var bytes = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(bytes, ct)) > 0)
                {
                    if (buffer.Length + count > NewsFeed.MaxBytes) throw new InvalidDataException("News response too large.");
                    buffer.Write(bytes, 0, count);
                }
                var parsed = Parse(buffer.ToArray(), source, batch, DateTime.UtcNow, page);
                rejected += parsed.Batch.Rejected;
                foreach (var entry in parsed.Batch.Entries)
                {
                    if (results.TryGetValue(entry.UrlHash, out var previous) && previous != entry)
                        throw new InvalidDataException("Conflicting news versions in one batch.");
                    results[entry.UrlHash] = entry;
                }
                if (page >= parsed.TotalPages) break;
            }
        }
        return new(results.Values.ToArray(), rejected, false, null, null);
    }

    public static (NewsBatch Batch, int TotalPages) Parse(byte[] bytes, NewsSource source,
        IReadOnlyCollection<string> requestedKeys, DateTime now, int expectedPage)
    {
        if (bytes.Length > NewsFeed.MaxBytes) throw new InvalidDataException("News response too large.");
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = json.RootElement;
            if (root.GetProperty("status").GetString() != "success") throw new InvalidDataException("Upstox rejected news request.");
            var pagination = root.GetProperty("metadata").GetProperty("page");
            var pages = pagination.GetProperty("total_pages").GetInt32();
            if (pagination.GetProperty("page_number").GetInt32() != expectedPage || pages < 0 || pages > 100)
                throw new InvalidDataException("Invalid news pagination.");
            var entries = new List<NewsEntry>();
            var rejected = 0;
            foreach (var group in root.GetProperty("data").EnumerateObject())
            {
                if (!requestedKeys.Contains(group.Name)) throw new InvalidDataException("Unexpected news instrument.");
                foreach (var article in group.Value.EnumerateArray())
                {
                    try
                    {
                        var headline = Plain(article.GetProperty("heading").GetString(), 500)
                            ?? throw new InvalidDataException("Missing plain headline.");
                        var published = DateTimeOffset.FromUnixTimeMilliseconds(article.GetProperty("published_time").GetInt64()).UtcDateTime;
                        if (published > now || published < now.AddDays(-Math.Min(7, Math.Clamp(source.RetentionDays, 1, 90))))
                            throw new InvalidDataException("News date outside source window.");
                        var link = article.GetProperty("article_link").GetString() ?? throw new InvalidDataException("Missing news link.");
                        var url = new UriBuilder(NewsUrlPolicy.Validate(link, "upstox.com")) { Fragment = "" }.Uri.AbsoluteUri;
                        var description = source.DescriptionAllowed && article.TryGetProperty("summary", out var summary)
                            ? Plain(summary.GetString(), 2000) : null;
                        entries.Add(new(Hash(url), Hash(url), url, headline, description, published, Hash(headline + "\n" + description + "\n" + url)));
                    }
                    catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException)
                    { rejected++; }
                }
            }
            return (new(entries, rejected, false, null, null), pages);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new InvalidDataException("Invalid Upstox news response."); }
    }

    private static string? Plain(string? value, int limit)
    {
        var text = WebUtility.HtmlDecode(value ?? "").Trim();
        return text.Length == 0 || text.Length > limit || text.Contains('<') || text.Contains('>') ? null : text;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class NewsFeedRouter(NewsFeed rss, UpstoxNewsFeed upstox) : INewsFeed
{
    public Task<NewsBatch> ReadAsync(NewsSource source, CancellationToken token) =>
        source.FeedUrl == UpstoxNewsFeed.Endpoint ? upstox.ReadAsync(source, token) : rss.ReadAsync(source, token);
}
