using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Options;

namespace IPOOnly.Scheduler.Financials;

public sealed class UpstoxFinancialClient(HttpClient client, IOptions<UpstoxOptions> options)
{
    public async Task<IReadOnlyList<FinancialObservation>> FetchAsync(string isin, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(isin, @"^IN[A-Z0-9]{10}$")) throw new InvalidDataException("Invalid ISIN.");
        foreach (var basis in new[] { "consolidated", "standalone" })
        {
            var rows = await FetchBasisAsync(isin, basis, cancellationToken);
            if (FinancialValidation.Usable(rows)) return rows;
        }
        return [];
    }

    private async Task<IReadOnlyList<FinancialObservation>> FetchBasisAsync(string isin, string basis, CancellationToken cancellationToken)
    {
        var url = $"https://api.upstox.com/v2/fundamentals/{isin}/income-statement?type={basis}&time_period=yearly";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AnalyticsToken);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (json.Length > 2_000_000) throw new InvalidDataException("Oversized fundamentals response.");
        return Parse(json, url, DateTimeOffset.UtcNow, basis);
    }

    public static IReadOnlyList<FinancialObservation> Parse(string json, string url, DateTimeOffset at, string expectedBasis = "consolidated")
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "success") throw new InvalidDataException("Unsuccessful fundamentals response.");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid fundamentals schema.");
        if (!data.TryGetProperty("units_in", out var unit) || unit.GetString() != "crore"
            || !data.TryGetProperty("type", out var basis) || basis.GetString() != expectedBasis
            || !data.TryGetProperty("time_period", out var frequency) || frequency.GetString() != "yearly")
            throw new InvalidDataException("Unsupported financial basis, period or units.");
        var rows = new List<FinancialObservation>();
        foreach (var metric in data.GetProperty("income_statement").EnumerateArray())
        {
            var code = metric.GetProperty("category").GetString();
            if (code is not ("revenue" or "operating_profit" or "net_profit")) continue;
            foreach (var point in metric.GetProperty("history").EnumerateArray())
            {
                if (!DateTime.TryParseExact(point.GetProperty("period").GetString(), "MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var period))
                    throw new InvalidDataException("Ambiguous financial period.");
                var raw = point.GetProperty("value");
                if (raw.ValueKind == JsonValueKind.Null) continue;
                if (!decimal.TryParse(raw.ToString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                    throw new InvalidDataException("Invalid financial value.");
                rows.Add(new(code, new DateOnly(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month)),
                    expectedBasis, "yearly", value, "INR", "crore", "Upstox", url, null, at));
            }
        }
        if (rows.Any(x => !FinancialValidation.ValidObservation(x)) || rows.GroupBy(x => (x.Metric, x.PeriodEnd)).Any(g => g.Select(x => x.Value).Distinct().Count() > 1))
            throw new InvalidDataException("Invalid or conflicting financial observations.");
        return FinancialValidation.Usable(rows) ? rows.DistinctBy(x => (x.Metric, x.PeriodEnd)).ToList() : [];
    }
}
