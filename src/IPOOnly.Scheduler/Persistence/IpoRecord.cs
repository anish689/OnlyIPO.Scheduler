namespace IPOOnly.Scheduler.Persistence;

public sealed record IpoRecord(
    string Slug,
    string CompanyName,
    string Status,
    string MarketType,
    int Exchanges,
    decimal? IssueSize,
    decimal? PriceBandMinimum,
    decimal? PriceBandMaximum,
    int? LotSize,
    decimal? MinimumInvestment,
    decimal? FaceValue,
    DateTimeOffset? OpenDate,
    DateTimeOffset? CloseDate,
    DateTimeOffset? AllotmentDate,
    DateTimeOffset? RefundDate,
    DateTimeOffset? DematCreditDate,
    DateTimeOffset? ListingDate,
    string? Registrar,
    decimal? OverallSubscription,
    string? DrhpDocumentUrl,
    string? RhpDocumentUrl,
    string SourceName,
    string SourceUrl,
    DateTimeOffset SourceUpdatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record IpoTimelineEventRecord(
    string EventType,
    string Label,
    DateTimeOffset? EventDate,
    string AvailabilityStatus,
    string SourceName,
    DateTimeOffset SourceUpdatedAt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record IpoDocumentRecord(
    string DocumentType,
    string Label,
    string Url,
    string SourceName,
    DateTimeOffset SourceUpdatedAt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record IpoSubscriptionSnapshotRecord(
    string InvestorCategory,
    decimal? SubscriptionTimes,
    string AvailabilityStatus,
    string SourceName,
    DateTimeOffset SourceUpdatedAt,
    DateTimeOffset CapturedAtUtc);

public sealed record IpoSourceSnapshotRecord(
    Guid? IpoId,
    string SourceName,
    string SourceRecordId,
    string SourceEndpoint,
    string SourceStatus,
    string PayloadJson,
    string PayloadHash,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset? SourceUpdatedAt);
