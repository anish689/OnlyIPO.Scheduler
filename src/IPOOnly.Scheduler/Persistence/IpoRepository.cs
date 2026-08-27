using Npgsql;

namespace IPOOnly.Scheduler.Persistence;

public sealed class IpoRepository(NpgsqlDataSource dataSource)
{
    public async Task UpsertAsync(IpoRecord ipo, CancellationToken cancellationToken)
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
                @Id, @Slug, @CompanyName, NULL, NULL, @Status, @MarketType, @Exchanges,
                NULL, @IssueSize, NULL, NULL, @PriceBandMinimum,
                @PriceBandMaximum, @LotSize, @MinimumInvestment, @FaceValue, @OpenDate, @CloseDate,
                @AllotmentDate, @RefundDate, @DematCreditDate, @ListingDate, @Registrar, NULL,
                NULL, NULL, NULL, NULL,
                NULL, NULL, NULL, NULL,
                @OverallSubscription, @DrhpDocumentUrl, @RhpDocumentUrl, NULL,
                NULL, @SourceName, @SourceUrl, @SourceUpdatedAt, @CreatedAt, @UpdatedAt)
            ON CONFLICT ("Slug") DO UPDATE SET
                "CompanyName" = EXCLUDED."CompanyName",
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
                "UpdatedAt" = EXCLUDED."UpdatedAt";
            """;

        await using var command = dataSource.CreateCommand(sql);
        Add(command, "Id", Guid.NewGuid());
        Add(command, "Slug", ipo.Slug);
        Add(command, "CompanyName", ipo.CompanyName);
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

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Add(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
