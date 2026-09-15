using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace IPOOnly.Scheduler.News;

public sealed record NewsSource(Guid Id, string FeedUrl, string ArticleHost, bool DescriptionAllowed,
    string? ETag, DateTime? LastModifiedUtc, int RetentionDays);
public sealed record NewsEntry(string ExternalIdHash, string UrlHash, string Url, string Headline,
    string? Description, DateTime PublishedAtUtc, string ContentHash);
public sealed record NewsBatch(IReadOnlyList<NewsEntry> Entries, int Rejected, bool NotModified, string? ETag, DateTime? LastModifiedUtc);
public interface INewsFeed { Task<NewsBatch> ReadAsync(NewsSource source, CancellationToken token); }

public static class NewsUrlPolicy
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8);
        return b[0] is not (0 or 10 or 127) && b[0] < 224
            && !(b[0] == 100 && b[1] is >= 64 and <= 127)
            && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31)
            && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2))
            && !(b[0] == 198 && b[1] is 18 or 19 or 51) && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }

    public static Uri Validate(string value, string? host = null)
    {
        if (value.Length > 2000 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.HostNameType != UriHostNameType.Dns
            || !uri.Host.Contains('.') || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || (host != null && !uri.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unapproved news URL.");
        return uri;
    }
}

public sealed class NewsFeed : INewsFeed
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public async Task<NewsBatch> ReadAsync(NewsSource source, CancellationToken token)
    {
        var uri = NewsUrlPolicy.Validate(source.FeedUrl);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectCallback = async (context, cancellation) =>
            {
                if (context.DnsEndPoint.Host != uri.IdnHost) throw new InvalidDataException("Unexpected news host.");
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellation);
                if (addresses.Length == 0 || addresses.Any(x => !NewsUrlPolicy.IsPublic(x))) throw new InvalidDataException("Non-public news host.");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(addresses, 443, cancellation); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("OnlyIPO-News/1.0");
            if (source.ETag != null) request.Headers.TryAddWithoutValidation("If-None-Match", source.ETag);
            if (source.LastModifiedUtc.HasValue) request.Headers.IfModifiedSince = source.LastModifiedUtc.Value;
            HttpResponseMessage response;
            try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException) when (attempt < 2) { await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct); continue; }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotModified) return new([], 0, true, source.ETag, source.LastModifiedUtc);
                if (attempt < 2 && ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests))
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(attempt + 1);
                    if (delay > TimeSpan.FromSeconds(10)) throw new HttpRequestException("Source requested later retry.");
                    await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, ct);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("News feed too large.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream();
                var bytes = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(bytes, ct)) > 0)
                {
                    if (buffer.Length + read > MaxBytes) throw new InvalidDataException("News feed too large.");
                    await buffer.WriteAsync(bytes.AsMemory(0, read), ct);
                }
                return Parse(buffer.ToArray(), source, DateTime.UtcNow) with
                { ETag = response.Headers.ETag?.ToString(), LastModifiedUtc = response.Content.Headers.LastModified?.UtcDateTime };
            }
        }
    }

    public static NewsBatch Parse(byte[] bytes, NewsSource source, DateTime now)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("News feed too large.");
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes });
        var feed = SyndicationFeed.Load(reader) ?? throw new InvalidDataException("Invalid feed.");
        var result = new List<NewsEntry>();
        var rejected = 0;
        foreach (var item in feed.Items.Take(200))
        {
            var headline = Plain(item.Title);
            var url = item.Links.FirstOrDefault(x => x.RelationshipType is "alternate" or null)?.Uri?.ToString();
            var published = item.PublishDate.UtcDateTime;
            if (headline == null || headline.Length > 500 || published > now || published < now.AddDays(-Math.Clamp(source.RetentionDays, 1, 90)) || url == null)
            { rejected++; continue; }
            Uri approved;
            try { approved = NewsUrlPolicy.Validate(url, source.ArticleHost); }
            catch (InvalidDataException) { rejected++; continue; }
            var canonical = new UriBuilder(approved) { Fragment = "" }.Uri.AbsoluteUri;
            var description = source.DescriptionAllowed ? Plain(item.Summary) : null;
            if (description?.Length > 2000) description = null;
            result.Add(new(Hash(item.Id ?? canonical), Hash(canonical), canonical, headline, description, published,
                Hash(headline + "\n" + description + "\n" + canonical)));
        }
        return new(result, rejected, false, null, null);
    }

    private static string? Plain(TextSyndicationContent? content)
    {
        if (content == null || content.Type != "text") return null;
        var text = Regex.Replace(WebUtility.HtmlDecode(content.Text ?? ""), @"\s+", " ").Trim();
        return text.Length == 0 || text.Contains('<') || text.Contains('>') ? null : text;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
