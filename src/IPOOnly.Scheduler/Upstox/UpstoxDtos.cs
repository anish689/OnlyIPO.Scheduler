using System.Text.Json.Serialization;
using System.Text.Json;

namespace IPOOnly.Scheduler.Upstox;

public sealed class UpstoxListResponse
{
    [JsonPropertyName("data")]
    public List<UpstoxIpoSummary>? Data { get; init; }

    [JsonPropertyName("meta_data")]
    public UpstoxMetaData? MetaData { get; init; }
}

public sealed class UpstoxDetailResponse
{
    [JsonPropertyName("data")]
    public UpstoxIpoDetail? Data { get; init; }
}

public sealed class UpstoxMetaData
{
    [JsonPropertyName("page")]
    public UpstoxPageMetadata? Page { get; init; }
}

public sealed class UpstoxPageMetadata
{
    [JsonPropertyName("page_number")]
    public int PageNumber { get; init; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; init; }
}

public sealed class UpstoxIpoSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("isin")]
    public string? Isin { get; init; }

    [JsonPropertyName("issue_type")]
    public string? IssueType { get; init; }

    [JsonPropertyName("issue_size")]
    public decimal? IssueSize { get; init; }

    [JsonPropertyName("minimum_price")]
    public decimal? MinimumPrice { get; init; }

    [JsonPropertyName("maximum_price")]
    public decimal? MaximumPrice { get; init; }

    [JsonPropertyName("bidding_start_date")]
    public string? BiddingStartDate { get; init; }

    [JsonPropertyName("bidding_end_date")]
    public string? BiddingEndDate { get; init; }

    [JsonPropertyName("total_subscription")]
    public decimal? TotalSubscription { get; init; }
}

public sealed class UpstoxIpoDetail
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("issue_type")]
    public string? IssueType { get; init; }

    [JsonPropertyName("issue_size")]
    public decimal? IssueSize { get; init; }

    [JsonPropertyName("minimum_price")]
    public decimal? MinimumPrice { get; init; }

    [JsonPropertyName("maximum_price")]
    public decimal? MaximumPrice { get; init; }

    [JsonPropertyName("bidding_start_date")]
    public string? BiddingStartDate { get; init; }

    [JsonPropertyName("bidding_end_date")]
    public string? BiddingEndDate { get; init; }

    [JsonPropertyName("face_value")]
    public decimal? FaceValue { get; init; }

    [JsonPropertyName("lot_size")]
    public int? LotSize { get; init; }

    [JsonPropertyName("minimum_quantity")]
    public int? MinimumQuantity { get; init; }

    [JsonPropertyName("cut_off_price")]
    public decimal? CutOffPrice { get; init; }

    [JsonPropertyName("listing_exchange")]
    public string? ListingExchange { get; init; }

    [JsonPropertyName("rhp_url")]
    public string? RhpUrl { get; init; }

    [JsonPropertyName("drhp_url")]
    public string? DrhpUrl { get; init; }

    [JsonPropertyName("allotment_date")]
    public string? AllotmentDate { get; init; }

    [JsonPropertyName("refund_initiation_date")]
    public string? RefundInitiationDate { get; init; }

    [JsonPropertyName("demat_transfer_date")]
    public string? DematTransferDate { get; init; }

    [JsonPropertyName("listing_date")]
    public string? ListingDate { get; init; }

    [JsonPropertyName("registrar_info")]
    public JsonElement? RegistrarInfo { get; init; }

    [JsonPropertyName("total_subscription")]
    public decimal? TotalSubscription { get; init; }
}
