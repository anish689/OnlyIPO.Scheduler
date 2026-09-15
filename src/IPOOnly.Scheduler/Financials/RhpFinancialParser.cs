using System.Globalization;
using System.Text.RegularExpressions;
using IPOOnly.Scheduler.Documents;

namespace IPOOnly.Scheduler.Financials;

// Conservative supported-table grammar. Unknown/scanned/multi-page layouts are withheld for review.
public sealed class RhpFinancialParser
{
    public IReadOnlyList<FinancialObservation> Parse(IReadOnlyList<PdfPageText> pages, string url, DateTimeOffset at)
    {
        var result = new List<FinancialObservation>();
        foreach (var page in pages)
        {
            var text = page.Text;
            if (!Regex.IsMatch(text, @"restated\s+(?:standalone|consolidated)\s+statement\s+of\s+profit\s+and\s+loss", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(text, @"peer|competitor|quarter|six months|nine months|three months", RegexOptions.IgnoreCase)) continue;
            var standalone = Regex.IsMatch(text, @"\bstandalone\b", RegexOptions.IgnoreCase);
            var consolidated = Regex.IsMatch(text, @"\bconsolidated\b", RegexOptions.IgnoreCase);
            if (standalone == consolidated) continue;
            var units = Regex.Matches(text, @"(?:INR|Rs\.?|₹|Rupees)\s*(?:in\s+)?(crores?|lakhs?|millions?)\b", RegexOptions.IgnoreCase)
                .Select(x => x.Groups[1].Value.ToLowerInvariant().TrimEnd('s')).Distinct().ToList();
            if (units.Count != 1) continue;
            var divisor = units[0] == "lakh" ? 100m : units[0] == "million" ? 10m : 1m;
            var lines = text.Split('\n');
            var header = lines.FirstOrDefault(x => Regex.IsMatch(x, @"years? ended", RegexOptions.IgnoreCase));
            if (header is null) continue;
            var matches = Regex.Matches(header, @"(?:March\s+31,?\s+\d{4}|31\s+March\s+\d{4})", RegexOptions.IgnoreCase);
            if (matches.Count is < 2 or > 4) continue;
            var periods = matches.Select(x => DateOnly.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
            if (periods.Distinct().Count() != periods.Length) continue;
            foreach (var line in lines)
            {
                var match = Regex.Match(line.Trim(), @"^(Revenue from operations|Profit after tax)\s+(.+)$", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var cells = Regex.Split(match.Groups[2].Value.Trim(), @"\s+");
                if (cells.Length != periods.Length) continue;
                var values = new List<decimal>();
                foreach (var cell in cells)
                {
                    // Footnote markers and unaligned note columns deliberately fail validation.
                    if (!Regex.IsMatch(cell, @"^(?:-?\d[\d,]*(?:\.\d+)?|\(\d[\d,]*(?:\.\d+)?\))$")) break;
                    if (!decimal.TryParse(cell, NumberStyles.Number | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture, out var value)) break;
                    values.Add(value / divisor);
                }
                if (values.Count != periods.Length) continue;
                for (var i = 0; i < periods.Length; i++) result.Add(new(
                    match.Groups[1].Value.StartsWith("Revenue", StringComparison.OrdinalIgnoreCase) ? "revenue_from_operations" : "profit_after_tax",
                    periods[i], standalone ? "standalone" : "consolidated", "yearly", values[i], "INR", "crore", "RHP", url, page.PageNumber, at));
            }
        }
        return FinancialValidation.Usable(result) ? result.DistinctBy(x => (x.Metric, x.PeriodEnd)).ToList() : [];
    }
}
