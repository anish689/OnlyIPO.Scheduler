using IPOOnly.Scheduler.Upstox;

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
}
