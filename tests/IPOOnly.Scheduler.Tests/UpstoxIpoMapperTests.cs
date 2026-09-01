using IPOOnly.Scheduler.Upstox;
using System.Text.Json;

namespace IPOOnly.Scheduler.Tests;

public sealed class UpstoxIpoMapperTests
{
    private readonly UpstoxIpoMapper _mapper = new();

    [Fact]
    public void Map_TreatsZeroPriceBandValuesAsUnknown()
    {
        var record = _mapper.Map(
            new UpstoxIpoSummary
            {
                Id = "sample-ipo",
                Name = "Sample IPO",
                Status = "open",
                IssueType = "regular",
                MinimumPrice = 0,
                MaximumPrice = 0
            },
            null,
            DateTimeOffset.UnixEpoch);

        Assert.Null(record.PriceBandMinimum);
        Assert.Null(record.PriceBandMaximum);
        Assert.Null(record.MinimumInvestment);
    }

    [Fact]
    public void Map_ComputesMinimumInvestmentFromKnownLotAndUpperBand()
    {
        var record = _mapper.Map(
            new UpstoxIpoSummary
            {
                Id = "sample-ipo",
                Name = "Sample IPO",
                Status = "upcoming",
                IssueType = "regular",
                MaximumPrice = 455
            },
            new UpstoxIpoDetail
            {
                Id = "sample-ipo",
                LotSize = 32,
                ListingExchange = "NSE,BSE"
            },
            DateTimeOffset.UnixEpoch);

        Assert.Equal(14560, record.MinimumInvestment);
        Assert.Equal(3, record.Exchanges);
        Assert.Equal("Upcoming", record.Status);
        Assert.Equal("Mainboard", record.MarketType);
    }

    [Fact]
    public void Map_MapsSmeAndListedStatus()
    {
        var record = _mapper.Map(
            new UpstoxIpoSummary
            {
                Id = "sme-ipo",
                Name = "SME IPO",
                Status = "listed",
                IssueType = "sme"
            },
            null,
            DateTimeOffset.UnixEpoch);

        Assert.Equal("Listed", record.Status);
        Assert.Equal("SME", record.MarketType);
    }

    [Fact]
    public void Map_ExtractsRegistrarFromStructuredRegistrarInfo()
    {
        using var document = JsonDocument.Parse("""{"name":"KFin Technologies Limited","phone":"1800"}""");

        var record = _mapper.Map(
            new UpstoxIpoSummary
            {
                Id = "registrar-ipo",
                Name = "Registrar IPO",
                Status = "open",
                IssueType = "regular"
            },
            new UpstoxIpoDetail
            {
                Id = "registrar-ipo",
                RegistrarInfo = document.RootElement.Clone()
            },
            DateTimeOffset.UnixEpoch);

        Assert.Equal("KFin Technologies Limited", record.Registrar);
    }

    [Fact]
    public void MapTimeline_MarksMissingDatesAsNotAnnounced()
    {
        var record = _mapper.Map(
            new UpstoxIpoSummary
            {
                Id = "timeline-ipo",
                Name = "Timeline IPO",
                Status = "open",
                IssueType = "regular",
                BiddingStartDate = "2026-09-01"
            },
            null,
            DateTimeOffset.UnixEpoch);

        var timeline = _mapper.MapTimeline(record, DateTimeOffset.UnixEpoch);

        Assert.Contains(timeline, x => x.EventType == "OpenDate" && x.AvailabilityStatus == "Available");
        Assert.Contains(timeline, x => x.EventType == "ListingDate" && x.AvailabilityStatus == "NotAnnounced");
    }

    [Fact]
    public void MapDocuments_IncludesOnlyAvailableDocumentLinks()
    {
        var record = _mapper.Map(
            new UpstoxIpoSummary
            {
                Id = "document-ipo",
                Name = "Document IPO",
                Status = "open",
                IssueType = "regular"
            },
            new UpstoxIpoDetail
            {
                Id = "document-ipo",
                RhpUrl = " https://example.com/rhp.pdf "
            },
            DateTimeOffset.UnixEpoch);

        var documents = _mapper.MapDocuments(record, DateTimeOffset.UnixEpoch);

        var document = Assert.Single(documents);
        Assert.Equal("RHP", document.DocumentType);
        Assert.Equal("https://example.com/rhp.pdf", document.Url);
    }

    [Fact]
    public void MapSubscriptionSnapshots_SeparatesProvidedAndUnsupportedCategories()
    {
        var record = _mapper.Map(
            new UpstoxIpoSummary
            {
                Id = "subscription-ipo",
                Name = "Subscription IPO",
                Status = "open",
                IssueType = "regular",
                TotalSubscription = 24.21m
            },
            null,
            DateTimeOffset.UnixEpoch);

        var snapshots = _mapper.MapSubscriptionSnapshots(record, DateTimeOffset.UnixEpoch);

        Assert.Contains(snapshots, x => x.InvestorCategory == "Overall" && x.SubscriptionTimes == 24.21m && x.AvailabilityStatus == "Available");
        Assert.Contains(snapshots, x => x.InvestorCategory == "Retail" && x.AvailabilityStatus == "NotProvidedBySource");
    }
}
