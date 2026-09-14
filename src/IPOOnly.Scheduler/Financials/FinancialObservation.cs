namespace IPOOnly.Scheduler.Financials;

public sealed record FinancialObservation(string Metric, DateOnly PeriodEnd, string Basis, string Frequency,
    decimal Value, string Currency, string Unit, string Source, string SourceUrl, int? PageNumber, DateTimeOffset RetrievedAtUtc);

public static class FinancialValidation
{
    public static bool IsRevenue(string metric) => metric is "revenue" or "revenue_from_operations";

    public static bool ValidObservation(FinancialObservation row) => row.Frequency == "yearly" && row.Unit == "crore" && row.Currency == "INR"
        && row.Basis is "consolidated" or "standalone"
        && row.Source is "Upstox" or "RHP"
        && row.Metric is "revenue" or "revenue_from_operations" or "net_profit" or "profit_after_tax" or "operating_profit"
        && row.PeriodEnd.Year >= 1990 && row.PeriodEnd <= DateOnly.FromDateTime(row.RetrievedAtUtc.UtcDateTime)
        && row.PeriodEnd.Day == DateTime.DaysInMonth(row.PeriodEnd.Year, row.PeriodEnd.Month)
        && Math.Abs(row.Value) < 1_000_000_000_000_000_000m
        && row.SourceUrl.Length <= 1000 && Uri.TryCreate(row.SourceUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0
        && (row.Source != "RHP" || row.PageNumber is > 0);

    public static bool Usable(IReadOnlyList<FinancialObservation> rows) => rows.Count > 0
        && rows.Select(x => (x.Source, x.Basis, x.Frequency, x.Currency, x.Unit)).Distinct().Count() == 1
        && rows.All(ValidObservation)
        && rows.Where(x => IsRevenue(x.Metric)).Select(x => x.PeriodEnd).Distinct().Count() >= 2
        && !rows.GroupBy(x => (x.Metric, x.PeriodEnd)).Any(g => g.Select(x => x.Value).Distinct().Count() > 1);
}
