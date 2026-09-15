using System.Net;
using IPOOnly.Scheduler.Documents;
using IPOOnly.Scheduler.Financials;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace IPOOnly.Scheduler.Tests;

public sealed class FinancialTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
    private const string Url = "https://api.upstox.com/v2/fundamentals/INE002A01018/income-statement";
    private const string Json = """
        {"status":"success","data":{"type":"consolidated","time_period":"yearly","units_in":"crore","income_statement":[
          {"category":"revenue","history":[{"period":"Mar 2025","value":100},{"period":"Mar 2024","value":80}]},
          {"category":"net_profit","history":[{"period":"Mar 2025","value":-2},{"period":"Mar 2024","value":0}]}
        ]}}
        """;
    private const string Table = """
        Restated standalone statement of profit and loss
        INR in lakhs
        Years ended March 31, 2025 March 31, 2024
        Revenue from operations 10,000.00 8,000.00
        Profit after tax (200.00) 0.00
        """;

    [Fact]
    public void Annual_api_preserves_negative_and_zero_values()
    {
        var rows = UpstoxFinancialClient.Parse(Json, Url, At);
        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, x => x.Metric == "net_profit" && x.Value == -2);
        Assert.Contains(rows, x => x.Metric == "net_profit" && x.Value == 0);
        Assert.All(rows, x => Assert.Equal("crore", x.Unit));
    }

    [Theory]
    [InlineData("yearly", "quarterly")]
    [InlineData("crore", "lakh")]
    [InlineData("Mar 2025", "TTM")]
    [InlineData("Mar 2025", "Mar 2099")]
    public void Incompatible_schema_is_not_empty_coverage(string from, string to) =>
        Assert.Throws<InvalidDataException>(() => UpstoxFinancialClient.Parse(Json.Replace(from, to), Url, At));

    [Fact]
    public void Conflicting_api_periods_fail_closed() => Assert.Throws<InvalidDataException>(() =>
        UpstoxFinancialClient.Parse(Json.Replace("Mar 2024", "Mar 2025"), Url, At));

    [Fact]
    public void Rhp_requires_aligned_periods_units_basis_and_preserves_citations()
    {
        var rows = ParseTable(Table);
        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, x => x.Metric == "revenue_from_operations" && x.Value == 100m);
        Assert.Contains(rows, x => x.Metric == "profit_after_tax" && x.Value == -2m);
        Assert.All(rows, x => Assert.Equal(12, x.PageNumber));
    }

    [Theory]
    [InlineData("INR in lakhs", "unknown units")]
    [InlineData("10,000.00 8,000.00", "7 10,000.00 8,000.00")]
    [InlineData("10,000.00", "10,000.00*")]
    [InlineData("Years ended", "Six months ended")]
    [InlineData("standalone", "standalone consolidated")]
    [InlineData("March 31, 2024", "March 31, 2025")]
    public void Ambiguous_rhp_is_withheld(string from, string to) => Assert.Empty(ParseTable(Table.Replace(from, to)));

    [Fact]
    public void Conflict_across_pages_is_not_published() => Assert.Empty(new RhpFinancialParser().Parse(
        [new(12, Table), new(13, Table.Replace("10,000.00", "11,000.00"))], "https://assets.upstox.com/rhp.pdf", At));

    [Fact]
    public async Task Upstox_wins_without_requesting_document()
    {
        var api = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Json) });
        var pdf = new Handler(_ => throw new InvalidOperationException("Must not download RHP"));
        Assert.Equal(4, (await Service(api, pdf).FetchAsync("INE002A01018", "https://assets.upstox.com/rhp.pdf", default)).Count);
        Assert.Equal(0, pdf.Calls);
    }

    [Fact]
    public async Task Empty_consolidated_tries_standalone_before_rhp()
    {
        var api = new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(
            request.RequestUri!.Query.Contains("standalone") ? Json.Replace("consolidated", "standalone") : Empty) });
        var pdf = new Handler(_ => throw new InvalidOperationException());
        var rows = await Service(api, pdf).FetchAsync("INE002A01018", "https://assets.upstox.com/rhp.pdf", default);
        Assert.All(rows, x => Assert.Equal("standalone", x.Basis));
        Assert.Equal(2, api.Calls);
        Assert.Equal(0, pdf.Calls);
    }

    [Fact]
    public async Task Failed_request_does_not_fallback()
    {
        var api = new Handler(_ => new(HttpStatusCode.Unauthorized));
        var pdf = new Handler(_ => throw new InvalidOperationException());
        await Assert.ThrowsAsync<HttpRequestException>(() => Service(api, pdf).FetchAsync("INE002A01018", "https://assets.upstox.com/rhp.pdf", default));
        Assert.Equal(0, pdf.Calls);
    }

    [Fact]
    public async Task Confirmed_empty_coverage_reaches_bounded_document_download()
    {
        var api = new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(
            request.RequestUri!.Query.Contains("standalone") ? Empty.Replace("consolidated", "standalone") : Empty) });
        var pdf = new Handler(_ => { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }; response.Content.Headers.ContentLength = 40_000_000; return response; });
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(api, pdf).FetchAsync("INE002A01018", "https://assets.upstox.com/rhp.pdf", default));
        Assert.Equal(1, pdf.Calls);
    }

    [Theory]
    [InlineData("http://assets.upstox.com/rhp.pdf")]
    [InlineData("https://assets.upstox.com.evil.test/rhp.pdf")]
    [InlineData("https://127.0.0.1/rhp.pdf")]
    [InlineData("https://user@assets.upstox.com/rhp.pdf")]
    public void Unsafe_document_urls_are_rejected(string url) => Assert.False(FinancialEnrichmentService.AllowedRhp(url));

    [Fact]
    public async Task Empty_api_falls_back_through_real_pdf_text_extraction()
    {
        var document = new PdfDocumentBuilder();
        var font = document.AddStandard14Font(Standard14Font.Helvetica);
        var page = document.AddPage(PageSize.A4);
        var y = 750;
        foreach (var line in Table.Split('\n')) { page.AddText(line, 10, new PdfPoint(30, y), font); y -= 24; }
        var pdf = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(document.Build()) });
        var api = new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(
            request.RequestUri!.Query.Contains("standalone") ? Empty.Replace("consolidated", "standalone") : Empty) });
        var rows = await Service(api, pdf).FetchAsync("INE002A01018", "https://assets.upstox.com/rhp.pdf", default);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, x => { Assert.Equal("RHP", x.Source); Assert.Equal(1, x.PageNumber); });
    }

    private const string Empty = """{"status":"success","data":{"type":"consolidated","time_period":"yearly","units_in":"crore","income_statement":[]}}""";
    private static IReadOnlyList<FinancialObservation> ParseTable(string text) => new RhpFinancialParser().Parse([new PdfPageText(12, text)], "https://assets.upstox.com/rhp.pdf", At);
    private static FinancialEnrichmentService Service(Handler api, Handler pdf) => new(
        new UpstoxFinancialClient(new HttpClient(api), Options.Create(new UpstoxOptions { AnalyticsToken = "test-only" })),
        new HttpClient(pdf), new RhpFinancialParser(), new FinancialStore(null!), new ConfigurationBuilder().Build(), NullLogger<FinancialEnrichmentService>.Instance);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(response(request)); }
    }
}
