using IPOOnly.Scheduler.Persistence;
using System.Text.Json;

namespace IPOOnly.Scheduler.Upstox;

public sealed class UpstoxIpoMapper
{
    private const string SourceName = "Upstox";
    private const string Available = "Available";
    private const string NotAnnounced = "NotAnnounced";
    private const string NotProvidedBySource = "NotProvidedBySource";

    public IpoRecord Map(UpstoxIpoSummary summary, UpstoxIpoDetail? detail, DateTimeOffset fetchedAtUtc)
    {
        var name = FirstNonBlank(detail?.Name, summary.Name, summary.Symbol, summary.Id);
        var maxPrice = KnownMoney(detail?.MaximumPrice ?? summary.MaximumPrice);
        var lotSize = detail?.LotSize ?? detail?.MinimumQuantity;

        return new IpoRecord(
            Slug: summary.Id,
            CompanyName: name,
            Description: BuildDescription(detail?.Industry),
            Status: MapStatus(FirstNonBlank(detail?.Status, summary.Status)),
            MarketType: MapMarketType(FirstNonBlank(detail?.IssueType, summary.IssueType)),
            Exchanges: MapExchanges(detail?.ListingExchange),
            IssueSize: KnownMoney(detail?.IssueSize ?? summary.IssueSize),
            PriceBandMinimum: KnownMoney(detail?.MinimumPrice ?? summary.MinimumPrice),
            PriceBandMaximum: maxPrice,
            LotSize: lotSize,
            MinimumInvestment: maxPrice.HasValue && lotSize.HasValue ? maxPrice.Value * lotSize.Value : null,
            FaceValue: KnownMoney(detail?.FaceValue),
            OpenDate: ParseIndianDate(detail?.Timeline?.ApplicationStartDate ?? detail?.BiddingStartDate ?? summary.BiddingStartDate),
            CloseDate: ParseIndianDate(detail?.Timeline?.ApplicationEndDate ?? detail?.BiddingEndDate ?? summary.BiddingEndDate),
            AllotmentDate: ParseIndianDate(detail?.Timeline?.AllotmentDate ?? detail?.AllotmentDate),
            RefundDate: ParseIndianDate(detail?.Timeline?.RefundInitiationDate ?? detail?.RefundInitiationDate),
            DematCreditDate: ParseIndianDate(detail?.Timeline?.DematTransferDate ?? detail?.DematTransferDate),
            ListingDate: ParseIndianDate(detail?.Timeline?.ListingDate ?? detail?.ListingDate),
            Registrar: ExtractRegistrar(detail?.RegistrarInfo),
            OverallSubscription: detail?.TotalSubscription ?? summary.TotalSubscription,
            DrhpDocumentUrl: BlankToNull(detail?.DrhpUrl),
            RhpDocumentUrl: BlankToNull(detail?.RhpUrl),
            SourceName: SourceName,
            SourceUrl: $"https://api.upstox.com/v2/ipos/{Uri.EscapeDataString(summary.Id)}",
            SourceUpdatedAt: fetchedAtUtc,
            CreatedAt: fetchedAtUtc,
            UpdatedAt: fetchedAtUtc);
    }

    public IReadOnlyList<IpoTimelineEventRecord> MapTimeline(IpoRecord ipo, DateTimeOffset fetchedAtUtc)
    {
        return
        [
            Timeline("OpenDate", "Open date", ipo.OpenDate, fetchedAtUtc),
            Timeline("CloseDate", "Close date", ipo.CloseDate, fetchedAtUtc),
            Timeline("AllotmentDate", "Allotment", ipo.AllotmentDate, fetchedAtUtc),
            Timeline("RefundDate", "Refund initiation", ipo.RefundDate, fetchedAtUtc),
            Timeline("DematCreditDate", "Demat credit", ipo.DematCreditDate, fetchedAtUtc),
            Timeline("ListingDate", "Listing", ipo.ListingDate, fetchedAtUtc)
        ];
    }

    public IReadOnlyList<IpoDocumentRecord> MapDocuments(IpoRecord ipo, DateTimeOffset fetchedAtUtc)
    {
        var documents = new List<IpoDocumentRecord>();

        AddDocument(documents, "DRHP", "Draft red herring prospectus", ipo.DrhpDocumentUrl, fetchedAtUtc);
        AddDocument(documents, "RHP", "Red herring prospectus", ipo.RhpDocumentUrl, fetchedAtUtc);

        return documents;
    }

    public IReadOnlyList<IpoSubscriptionSnapshotRecord> MapSubscriptionSnapshots(IpoRecord ipo, DateTimeOffset fetchedAtUtc)
    {
        return
        [
            Subscription("Retail", null, NotProvidedBySource, fetchedAtUtc),
            Subscription("QIB", null, NotProvidedBySource, fetchedAtUtc),
            Subscription("NII", null, NotProvidedBySource, fetchedAtUtc),
            Subscription("Employee", null, NotProvidedBySource, fetchedAtUtc),
            Subscription(
                "Overall",
                ipo.OverallSubscription,
                ipo.OverallSubscription.HasValue ? Available : NotAnnounced,
                fetchedAtUtc)
        ];
    }

    private static IpoTimelineEventRecord Timeline(
        string eventType,
        string label,
        DateTimeOffset? eventDate,
        DateTimeOffset fetchedAtUtc)
    {
        return new IpoTimelineEventRecord(
            eventType,
            label,
            eventDate,
            eventDate.HasValue ? Available : NotAnnounced,
            SourceName,
            fetchedAtUtc,
            fetchedAtUtc,
            fetchedAtUtc);
    }

    private static void AddDocument(
        List<IpoDocumentRecord> documents,
        string documentType,
        string label,
        string? url,
        DateTimeOffset fetchedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        documents.Add(new IpoDocumentRecord(
            documentType,
            label,
            url.Trim(),
            SourceName,
            fetchedAtUtc,
            fetchedAtUtc,
            fetchedAtUtc));
    }

    private static IpoSubscriptionSnapshotRecord Subscription(
        string investorCategory,
        decimal? subscriptionTimes,
        string availabilityStatus,
        DateTimeOffset fetchedAtUtc)
    {
        return new IpoSubscriptionSnapshotRecord(
            investorCategory,
            subscriptionTimes,
            availabilityStatus,
            SourceName,
            fetchedAtUtc,
            fetchedAtUtc);
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

    private static string? BuildDescription(string? industry)
    {
        var value = BlankToNull(industry);
        return value is null ? null : $"Industry: {value}";
    }

    private static string? ExtractRegistrar(JsonElement? value)
    {
        if (value is null)
        {
            return null;
        }

        var element = value.Value;

        if (element.ValueKind == JsonValueKind.String)
        {
            return BlankToNull(element.GetString());
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "name", "registrar_name", "company_name" })
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                var registrar = BlankToNull(property.GetString());
                if (registrar is not null)
                {
                    return registrar;
                }
            }
        }

        return element.GetRawText();
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
