using System.Net;
using System.Text;
using System.Xml;
using IPOOnly.Scheduler.News;
using Xunit;

namespace IPOOnly.Scheduler.Tests;

public sealed class NewsTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly NewsSource Source = new(Guid.NewGuid(), "https://example.com/feed", "example.com", true, null, null, 90);
    private static byte[] Feed(string date = "Sun, 13 Sep 2026 10:00:00 GMT", string url = "https://example.com/News?Id=A", string title = "IPO update") =>
        Encoding.UTF8.GetBytes($"<rss version='2.0'><channel><title>Test</title><link>https://example.com</link><description>Test</description><item><guid>a</guid><title>{title}</title><link>{url}</link><pubDate>{date}</pubDate></item></channel></rss>");

    [Fact] public void Parses_rss_without_rewriting_case_sensitive_urls()
    { var result = NewsFeed.Parse(Feed(), Source, Now); Assert.Single(result.Entries); Assert.Equal("https://example.com/News?Id=A", result.Entries[0].Url); Assert.Equal(Now.AddHours(-2), result.Entries[0].PublishedAtUtc); }
    [Fact] public void Parses_atom() {
        var xml = "<feed xmlns='http://www.w3.org/2005/Atom'><title>Test</title><id>feed</id><updated>2026-09-13T10:00:00Z</updated><entry><id>a</id><title>IPO update</title><published>2026-09-13T10:00:00Z</published><link href='https://example.com/a'/></entry></feed>";
        Assert.Single(NewsFeed.Parse(Encoding.UTF8.GetBytes(xml), Source, Now).Entries);
    }
    [Theory][InlineData("")][InlineData("Mon, 14 Sep 2026 10:00:00 GMT")][InlineData("Tue, 01 Jan 2019 10:00:00 GMT")]
    public void Rejects_unusable_dates(string date) { Assert.Empty(NewsFeed.Parse(Feed(date), Source, Now).Entries); }
    [Theory][InlineData("http://example.com/a")][InlineData("https://evil.com/a")][InlineData("https://user:pass@example.com/a")][InlineData("https://127.0.0.1/a")][InlineData("https://example.com:8443/a")]
    public void Rejects_unapproved_article_links(string url) { Assert.Empty(NewsFeed.Parse(Feed(url: url), Source, Now).Entries); }
    [Theory][InlineData("127.0.0.1")][InlineData("10.0.0.1")][InlineData("169.254.169.254")][InlineData("100.64.0.1")][InlineData("::1")][InlineData("fc00::1")][InlineData("::ffff:192.168.0.1")]
    public void Rejects_private_destinations(string address) { Assert.False(NewsUrlPolicy.IsPublic(IPAddress.Parse(address))); }
    [Fact] public void Rejects_dtd_and_oversize() {
        Assert.Throws<XmlException>(() => NewsFeed.Parse(Encoding.UTF8.GetBytes("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><rss>&x;</rss>"), Source, Now));
        Assert.Throws<InvalidDataException>(() => NewsFeed.Parse(new byte[NewsFeed.MaxBytes + 1], Source, Now));
    }
    [Fact] public void Does_not_publish_markup_as_a_headline() { Assert.Empty(NewsFeed.Parse(Feed(title: "&lt;script&gt;test&lt;/script&gt;"), Source, Now).Entries); }
    [Fact] public void Hashes_are_repeatable() { Assert.Equal(NewsFeed.Parse(Feed(), Source, Now).Entries[0], NewsFeed.Parse(Feed(), Source, Now).Entries[0]); }
}
