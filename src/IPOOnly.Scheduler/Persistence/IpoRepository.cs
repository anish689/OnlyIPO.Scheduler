using Npgsql;

namespace IPOOnly.Scheduler.Persistence;

public sealed class IpoRepository(NpgsqlDataSource dataSource)
{
    public async Task<Guid> UpsertAsync(IpoRecord ipo, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ipos (
                "Id", "Slug", "CompanyName", "LogoUrl", "Description", "Status", "MarketType", "Exchanges",
                "OfferType", "IssueSize", "FreshIssueAmount", "OfferForSaleAmount", "PriceBandMinimum",
                "PriceBandMaximum", "LotSize", "MinimumInvestment", "FaceValue", "OpenDate", "CloseDate",
                "AllotmentDate", "RefundDate", "DematCreditDate", "ListingDate", "Registrar", "LeadManagers",
                "RetailQuotaPercentage", "QibQuotaPercentage", "NiiQuotaPercentage", "EmployeeQuotaPercentage",
                "RetailSubscription", "QibSubscription", "NiiSubscription", "EmployeeSubscription",
                "OverallSubscription", "DrhpDocumentUrl", "RhpDocumentUrl", "ExchangeAnnouncementUrl",
                "CompanyWebsiteUrl", "SourceName", "SourceUrl", "SourceUpdatedAt", "CreatedAt", "UpdatedAt")
            VALUES (
                @Id, @Slug, @CompanyName, NULL, @Description, @Status, @MarketType, @Exchanges,
                NULL, @IssueSize, NULL, NULL, @PriceBandMinimum,
                @PriceBandMaximum, @LotSize, @MinimumInvestment, @FaceValue, @OpenDate, @CloseDate,
                @AllotmentDate, @RefundDate, @DematCreditDate, @ListingDate, @Registrar, NULL,
                NULL, NULL, NULL, NULL,
                NULL, NULL, NULL, NULL,
                @OverallSubscription, @DrhpDocumentUrl, @RhpDocumentUrl, NULL,
                NULL, @SourceName, @SourceUrl, @SourceUpdatedAt, @CreatedAt, @UpdatedAt)
            ON CONFLICT ("Slug") DO UPDATE SET
                "CompanyName" = EXCLUDED."CompanyName",
                "Description" = EXCLUDED."Description",
                "Status" = EXCLUDED."Status",
                "MarketType" = EXCLUDED."MarketType",
                "Exchanges" = EXCLUDED."Exchanges",
                "IssueSize" = EXCLUDED."IssueSize",
                "PriceBandMinimum" = EXCLUDED."PriceBandMinimum",
                "PriceBandMaximum" = EXCLUDED."PriceBandMaximum",
                "LotSize" = EXCLUDED."LotSize",
                "MinimumInvestment" = EXCLUDED."MinimumInvestment",
                "FaceValue" = EXCLUDED."FaceValue",
                "OpenDate" = EXCLUDED."OpenDate",
                "CloseDate" = EXCLUDED."CloseDate",
                "AllotmentDate" = EXCLUDED."AllotmentDate",
                "RefundDate" = EXCLUDED."RefundDate",
                "DematCreditDate" = EXCLUDED."DematCreditDate",
                "ListingDate" = EXCLUDED."ListingDate",
                "Registrar" = EXCLUDED."Registrar",
                "OverallSubscription" = EXCLUDED."OverallSubscription",
                "DrhpDocumentUrl" = EXCLUDED."DrhpDocumentUrl",
                "RhpDocumentUrl" = EXCLUDED."RhpDocumentUrl",
                "SourceName" = EXCLUDED."SourceName",
                "SourceUrl" = EXCLUDED."SourceUrl",
                "SourceUpdatedAt" = EXCLUDED."SourceUpdatedAt",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            RETURNING "Id";
            """;

        await using var command = dataSource.CreateCommand(sql);
        var id = Guid.NewGuid();
        Add(command, "Id", id);
        Add(command, "Slug", ipo.Slug);
        Add(command, "CompanyName", ipo.CompanyName);
        Add(command, "Description", ipo.Description);
        Add(command, "Status", ipo.Status);
        Add(command, "MarketType", ipo.MarketType);
        Add(command, "Exchanges", ipo.Exchanges);
        Add(command, "IssueSize", ipo.IssueSize);
        Add(command, "PriceBandMinimum", ipo.PriceBandMinimum);
        Add(command, "PriceBandMaximum", ipo.PriceBandMaximum);
        Add(command, "LotSize", ipo.LotSize);
        Add(command, "MinimumInvestment", ipo.MinimumInvestment);
        Add(command, "FaceValue", ipo.FaceValue);
        Add(command, "OpenDate", ipo.OpenDate);
        Add(command, "CloseDate", ipo.CloseDate);
        Add(command, "AllotmentDate", ipo.AllotmentDate);
        Add(command, "RefundDate", ipo.RefundDate);
        Add(command, "DematCreditDate", ipo.DematCreditDate);
        Add(command, "ListingDate", ipo.ListingDate);
        Add(command, "Registrar", ipo.Registrar);
        Add(command, "OverallSubscription", ipo.OverallSubscription);
        Add(command, "DrhpDocumentUrl", ipo.DrhpDocumentUrl);
        Add(command, "RhpDocumentUrl", ipo.RhpDocumentUrl);
        Add(command, "SourceName", ipo.SourceName);
        Add(command, "SourceUrl", ipo.SourceUrl);
        Add(command, "SourceUpdatedAt", ipo.SourceUpdatedAt);
        Add(command, "CreatedAt", ipo.CreatedAt);
        Add(command, "UpdatedAt", ipo.UpdatedAt);

        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("IPO upsert did not return an id."));
    }

    public async Task ReplaceTimelineEventsAsync(
        Guid ipoId,
        IReadOnlyList<IpoTimelineEventRecord> events,
        CancellationToken cancellationToken)
    {
        const string deleteSql = """DELETE FROM "IpoTimelineEvents" WHERE "IpoId" = @IpoId;""";
        await using (var deleteCommand = dataSource.CreateCommand(deleteSql))
        {
            Add(deleteCommand, "IpoId", ipoId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = """
            INSERT INTO "IpoTimelineEvents" (
                "Id", "IpoId", "EventType", "Label", "EventDate", "AvailabilityStatus",
                "SourceName", "SourceUpdatedAt", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES (
                @Id, @IpoId, @EventType, @Label, @EventDate, @AvailabilityStatus,
                @SourceName, @SourceUpdatedAt, @CreatedAtUtc, @UpdatedAtUtc);
            """;

        foreach (var item in events)
        {
            await using var command = dataSource.CreateCommand(insertSql);
            Add(command, "Id", Guid.NewGuid());
            Add(command, "IpoId", ipoId);
            Add(command, "EventType", item.EventType);
            Add(command, "Label", item.Label);
            Add(command, "EventDate", item.EventDate);
            Add(command, "AvailabilityStatus", item.AvailabilityStatus);
            Add(command, "SourceName", item.SourceName);
            Add(command, "SourceUpdatedAt", item.SourceUpdatedAt);
            Add(command, "CreatedAtUtc", item.CreatedAtUtc);
            Add(command, "UpdatedAtUtc", item.UpdatedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task ReplaceDocumentsAsync(
        Guid ipoId,
        IReadOnlyList<IpoDocumentRecord> documents,
        CancellationToken cancellationToken)
    {
        const string deleteSql = """DELETE FROM "IpoDocuments" WHERE "IpoId" = @IpoId;""";
        await using (var deleteCommand = dataSource.CreateCommand(deleteSql))
        {
            Add(deleteCommand, "IpoId", ipoId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = """
            INSERT INTO "IpoDocuments" (
                "Id", "IpoId", "DocumentType", "Label", "Url", "SourceName",
                "SourceUpdatedAt", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES (
                @Id, @IpoId, @DocumentType, @Label, @Url, @SourceName,
                @SourceUpdatedAt, @CreatedAtUtc, @UpdatedAtUtc);
            """;

        foreach (var item in documents)
        {
            await using var command = dataSource.CreateCommand(insertSql);
            Add(command, "Id", Guid.NewGuid());
            Add(command, "IpoId", ipoId);
            Add(command, "DocumentType", item.DocumentType);
            Add(command, "Label", item.Label);
            Add(command, "Url", item.Url);
            Add(command, "SourceName", item.SourceName);
            Add(command, "SourceUpdatedAt", item.SourceUpdatedAt);
            Add(command, "CreatedAtUtc", item.CreatedAtUtc);
            Add(command, "UpdatedAtUtc", item.UpdatedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task InsertSubscriptionSnapshotsAsync(
        Guid ipoId,
        IReadOnlyList<IpoSubscriptionSnapshotRecord> snapshots,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO "IpoSubscriptionSnapshots" (
                "Id", "IpoId", "InvestorCategory", "SubscriptionTimes", "AvailabilityStatus",
                "SourceName", "SourceUpdatedAt", "CapturedAtUtc")
            VALUES (
                @Id, @IpoId, @InvestorCategory, @SubscriptionTimes, @AvailabilityStatus,
                @SourceName, @SourceUpdatedAt, @CapturedAtUtc)
            ON CONFLICT ("IpoId", "InvestorCategory", "CapturedAtUtc") DO UPDATE SET
                "SubscriptionTimes" = EXCLUDED."SubscriptionTimes",
                "AvailabilityStatus" = EXCLUDED."AvailabilityStatus",
                "SourceName" = EXCLUDED."SourceName",
                "SourceUpdatedAt" = EXCLUDED."SourceUpdatedAt";
            """;

        foreach (var item in snapshots)
        {
            await using var command = dataSource.CreateCommand(sql);
            Add(command, "Id", Guid.NewGuid());
            Add(command, "IpoId", ipoId);
            Add(command, "InvestorCategory", item.InvestorCategory);
            Add(command, "SubscriptionTimes", item.SubscriptionTimes);
            Add(command, "AvailabilityStatus", item.AvailabilityStatus);
            Add(command, "SourceName", item.SourceName);
            Add(command, "SourceUpdatedAt", item.SourceUpdatedAt);
            Add(command, "CapturedAtUtc", item.CapturedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task UpsertSourceSnapshotAsync(IpoSourceSnapshotRecord snapshot, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO "IpoSourceSnapshots" (
                "Id", "IpoId", "SourceName", "SourceRecordId", "SourceEndpoint", "SourceStatus",
                "PayloadJson", "PayloadHash", "CapturedAtUtc", "SourceUpdatedAt")
            VALUES (
                @Id, @IpoId, @SourceName, @SourceRecordId, @SourceEndpoint, @SourceStatus,
                CAST(@PayloadJson AS jsonb), @PayloadHash, @CapturedAtUtc, @SourceUpdatedAt)
            ON CONFLICT ("SourceName", "SourceRecordId", "SourceEndpoint", "PayloadHash") DO NOTHING;
            """;

        await using var command = dataSource.CreateCommand(sql);
        Add(command, "Id", Guid.NewGuid());
        Add(command, "IpoId", snapshot.IpoId);
        Add(command, "SourceName", snapshot.SourceName);
        Add(command, "SourceRecordId", snapshot.SourceRecordId);
        Add(command, "SourceEndpoint", snapshot.SourceEndpoint);
        Add(command, "SourceStatus", snapshot.SourceStatus);
        Add(command, "PayloadJson", snapshot.PayloadJson);
        Add(command, "PayloadHash", snapshot.PayloadHash);
        Add(command, "CapturedAtUtc", snapshot.CapturedAtUtc);
        Add(command, "SourceUpdatedAt", snapshot.SourceUpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Add(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
