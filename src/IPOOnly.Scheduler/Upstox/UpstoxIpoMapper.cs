using IPOOnly.Scheduler.Persistence;

namespace IPOOnly.Scheduler.Upstox;

public sealed class UpstoxIpoMapper
{
    public IpoRecord Map(UpstoxIpoSummary summary, UpstoxIpoDetail? detail, DateTimeOffset fetchedAtUtc)
    {
        var name = FirstNonBlank(detail?.Name, summary.Name, summary.Symbol, summary.Id);
        var maxPrice = KnownMoney(detail?.MaximumPrice ?? summary.MaximumPrice);
        var lotSize = detail?.LotSize ?? detail?.MinimumQuantity;

        return new IpoRecord(
            Slug: summary.Id,
            CompanyName: name,
            Status: MapStatus(FirstNonBlank(detail?.Status, summary.Status)),
            MarketType: MapMarketType(FirstNonBlank(detail?.IssueType, summary.IssueType)),
            Exchanges: MapExchanges(detail?.ListingExchange),
            IssueSize: KnownMoney(detail?.IssueSize ?? summary.IssueSize),
            PriceBandMinimum: KnownMoney(detail?.MinimumPrice ?? summary.MinimumPrice),
            PriceBandMaximum: maxPrice,
            LotSize: lotSize,
            MinimumInvestment: maxPrice.HasValue && lotSize.HasValue ? maxPrice.Value * lotSize.Value : null,
            FaceValue: KnownMoney(detail?.FaceValue),
            OpenDate: ParseIndianDate(detail?.BiddingStartDate ?? summary.BiddingStartDate),
            CloseDate: ParseIndianDate(detail?.BiddingEndDate ?? summary.BiddingEndDate),
            AllotmentDate: ParseIndianDate(detail?.AllotmentDate),
            RefundDate: ParseIndianDate(detail?.RefundInitiationDate),
            DematCreditDate: ParseIndianDate(detail?.DematTransferDate),
            ListingDate: ParseIndianDate(detail?.ListingDate),
            Registrar: BlankToNull(detail?.RegistrarInfo),
            OverallSubscription: detail?.TotalSubscription ?? summary.TotalSubscription,
            DrhpDocumentUrl: BlankToNull(detail?.DrhpUrl),
            RhpDocumentUrl: BlankToNull(detail?.RhpUrl),
            SourceName: "Upstox",
            SourceUrl: $"https://api.upstox.com/v2/ipos/{Uri.EscapeDataString(summary.Id)}",
            SourceUpdatedAt: fetchedAtUtc,
            CreatedAt: fetchedAtUtc,
            UpdatedAt: fetchedAtUtc);
    }

    private static string MapStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "upcoming" => "Upcoming",
            "open" => "Open",
            "closed" => "Closed",
            "listed" => "Listed",
            "withdrawn" => "Withdrawn",
            _ => "Draft"
        };
    }

    private static string MapMarketType(string? issueType)
    {
        return issueType?.Trim().ToLowerInvariant() switch
        {
            "sme" => "SME",
            _ => "Mainboard"
        };
    }

    private static int MapExchanges(string? exchange)
    {
        if (string.IsNullOrWhiteSpace(exchange))
        {
            return 0;
        }

        var normalized = exchange.ToUpperInvariant();
        var value = 0;

        if (normalized.Contains("NSE", StringComparison.Ordinal))
        {
            value |= 1;
        }

        if (normalized.Contains("BSE", StringComparison.Ordinal))
        {
            value |= 2;
        }

        return value;
    }

    private static decimal? KnownMoney(decimal? value)
    {
        return value is > 0 ? value : null;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
            ?? throw new InvalidOperationException("IPO name/id payload is incomplete.");
    }

    private static string? BlankToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTimeOffset? ParseIndianDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, out var date))
        {
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
    }
}
