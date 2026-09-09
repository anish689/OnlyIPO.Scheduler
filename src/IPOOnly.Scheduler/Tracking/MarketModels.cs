using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPOOnly.Scheduler.Tracking;

public sealed record EquityInstrument(
    [property: JsonPropertyName("exchange")] string Exchange,
    [property: JsonPropertyName("segment")] string Segment,
    [property: JsonPropertyName("trading_symbol")] string Symbol,
    [property: JsonPropertyName("isin")] string Isin,
    [property: JsonPropertyName("instrument_key")] string Key);
public sealed record TrackingCandidate(Guid IpoId, string Slug, DateOnly ListingDate, int Exchanges,
    string? Isin, string? Symbol, Guid? InstrumentId, string? InstrumentKey, DateOnly? LatestDate);
public sealed record DailyCandle(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
public sealed record CandleBatch(string Endpoint, string RawJson, IReadOnlyList<DailyCandle> Candles);

public static class MarketDataParser
{
    public static EquityInstrument? Match(TrackingCandidate ipo, IEnumerable<EquityInstrument> master)
    {
        // An ISIN mismatch must never fall back to a company name or symbol guess.
        var matches = master.Where(x => !string.IsNullOrWhiteSpace(x.Isin) && x.Isin.Length == 12 &&
            x.Segment == x.Exchange + "_EQ" && x.Key == x.Segment + "|" + x.Isin &&
            !string.IsNullOrWhiteSpace(x.Symbol) &&
            ((x.Exchange == "NSE" && (ipo.Exchanges & 1) != 0) || (x.Exchange == "BSE" && (ipo.Exchanges & 2) != 0)) &&
            (!string.IsNullOrWhiteSpace(ipo.Isin)
                ? string.Equals(x.Isin, ipo.Isin, StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrWhiteSpace(ipo.Symbol) && string.Equals(x.Symbol, ipo.Symbol, StringComparison.OrdinalIgnoreCase)))
            .Distinct().ToArray();
        foreach (var exchange in new[] { "NSE", "BSE" })
        {
            var selected = matches.Where(x => x.Exchange == exchange).ToArray();
            if (selected.Length > 1) return null;
            if (selected.Length == 1) return selected[0];
        }
        return null;
    }

    public static IReadOnlyList<DailyCandle> Parse(string raw, DateOnly from, DateOnly to)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        if (root.GetProperty("status").GetString() != "success") throw new InvalidDataException("Market provider did not return success.");
        var result = new List<DailyCandle>();
        var dates = new HashSet<DateOnly>();
        foreach (var row in root.GetProperty("data").GetProperty("candles").EnumerateArray())
        {
            if (row.GetArrayLength() < 6) throw new InvalidDataException("Incomplete candle.");
            var timestamp = row[0].GetString() ?? "";
            if (!timestamp.EndsWith("+05:30", StringComparison.Ordinal) ||
                !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
                throw new InvalidDataException("Daily candle requires an India-offset timestamp.");
            var date = DateOnly.FromDateTime(instant.DateTime);
            var candle = new DailyCandle(date, row[1].GetDecimal(), row[2].GetDecimal(), row[3].GetDecimal(), row[4].GetDecimal(), row[5].GetInt64());
            if (date < from || date > to || !dates.Add(date) || candle.Open <= 0 || candle.Close <= 0 || candle.Low <= 0 ||
                candle.High < Math.Max(candle.Open, candle.Close) || candle.Low > Math.Min(candle.Open, candle.Close) ||
                candle.High < candle.Low || candle.Volume < 0 || instant.TimeOfDay != TimeSpan.Zero)
                throw new InvalidDataException("Invalid or duplicate daily candle.");
            result.Add(candle);
        }
        return result.OrderBy(x => x.Date).ToArray();
    }
}
