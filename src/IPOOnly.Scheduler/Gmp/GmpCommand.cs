using Microsoft.Extensions.Configuration;
using Npgsql;

namespace IPOOnly.Scheduler.Gmp;

public sealed record GmpMapping(string IpoSlug, string FeedSlug);

public static class GmpCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration, bool preview, CancellationToken token)
    {
        if (!preview && !configuration.GetValue<bool>("Gmp:Enabled"))
            throw new InvalidOperationException("GMP ingestion is disabled.");
        var now = DateTimeOffset.UtcNow;
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1_000_000 };
        var feed = await GmpTodayFeed.FetchAsync(client, now, token);
        Console.WriteLine($"GMP feed: {feed.Ipos.Length} sample issues; source timestamp {feed.UpdatedAt:O}; license CC-BY-4.0.");
        if (preview)
        {
            Console.WriteLine("Preview only. No database opened or values published.");
            return 0;
        }
        var mappings = configuration.GetSection("Gmp:Mappings").Get<GmpMapping[]>() ?? [];
        if (mappings.Length == 0 || mappings.Length > 200
            || mappings.Any(x => x is null || string.IsNullOrWhiteSpace(x.IpoSlug) || string.IsNullOrWhiteSpace(x.FeedSlug))
            || mappings.Select(x => x.IpoSlug).Distinct().Count() != mappings.Length
            || mappings.Select(x => x.FeedSlug).Distinct().Count() != mappings.Length)
            throw new InvalidOperationException("GMP requires unique reviewed mappings.");
        var connectionString = configuration.GetConnectionString("IPOOnlyDatabase") ?? throw new InvalidOperationException("Database required.");
        if (configuration["DeploymentTarget"] == "production") ProductionConnectionGuard.Validate(connectionString);
        await using var source = NpgsqlDataSource.Create(connectionString);
        var (stored, withheld) = await SaveAsync(source, mappings, feed, now, token);
        Console.WriteLine($"GMP refresh completed: {stored} stored; {withheld} withheld. Missing samples retain their original expiry.");
        return 0;
    }

    public static async Task<(int Stored, int Withheld)> SaveAsync(NpgsqlDataSource source,
        IReadOnlyList<GmpMapping> mappings, GmpFeed feed, DateTimeOffset now, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var gate = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(1800261118)", connection, transaction))
            if (await gate.ExecuteScalarAsync(token) is not true) throw new InvalidOperationException("GMP refresh already running.");
        await using (var limits = new NpgsqlCommand("SET LOCAL lock_timeout = '2s'; SET LOCAL statement_timeout = '5s'", connection, transaction))
            await limits.ExecuteNonQueryAsync(token);
        var stored = 0;
        var withheld = 0;
        foreach (var mapping in mappings)
        {
            // Deliberately no fuzzy matching: a slug mapping and matching issue terms are required.
            await using var target = new NpgsqlCommand("""
                SELECT "Id", ("OpenDate" AT TIME ZONE 'Asia/Kolkata')::date, "PriceBandMaximum"
                FROM ipos WHERE "Slug" = @slug AND "Status" IN ('Open','Closed')
                AND ("OpenDate" AT TIME ZONE 'Asia/Kolkata')::date <= (@now AT TIME ZONE 'Asia/Kolkata')::date
                AND ("ListingDate" IS NULL OR ("ListingDate" AT TIME ZONE 'Asia/Kolkata')::date > (@now AT TIME ZONE 'Asia/Kolkata')::date)
                """, connection, transaction);
            target.Parameters.AddWithValue("slug", mapping.IpoSlug); target.Parameters.AddWithValue("now", now);
            Guid id; DateOnly openDate; decimal price;
            await using (var reader = await target.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token) || reader.IsDBNull(1) || reader.IsDBNull(2)) { withheld++; continue; }
                id = reader.GetGuid(0); openDate = reader.GetFieldValue<DateOnly>(1); price = reader.GetDecimal(2);
            }
            var entry = feed.Ipos.SingleOrDefault(x => x.Slug == mapping.FeedSlug);
            if (entry is null) { withheld++; continue; }
            if (!GmpTodayFeed.IsUsable(entry, feed, openDate, price, now))
            {
                await using var revoke = new NpgsqlCommand("""
                    UPDATE "IpoGmpQuotes" SET "IsApprovedForDisplay" = false
                    WHERE "IpoId" = @id AND "ObservedAtUtc" <= @reported
                    """, connection, transaction);
                revoke.Parameters.AddWithValue("id", id); revoke.Parameters.AddWithValue("reported", feed.UpdatedAt.ToUniversalTime());
                await revoke.ExecuteNonQueryAsync(token); withheld++; continue;
            }
            await using var save = new NpgsqlCommand("""
                INSERT INTO "IpoGmpQuotes" ("IpoId","Premium","RangeLow","RangeHigh","SourceCount","ObservedAtUtc","ReviewedAtUtc","SourceName","SourceUrl","RightsReference","IsApprovedForDisplay")
                VALUES (@id,@premium,@low,@high,@count,@reported,@now,'gmptoday.in',@url,@rights,true)
                ON CONFLICT ("IpoId") DO UPDATE SET
                  "Premium"=EXCLUDED."Premium", "RangeLow"=EXCLUDED."RangeLow", "RangeHigh"=EXCLUDED."RangeHigh",
                  "SourceCount"=EXCLUDED."SourceCount", "ObservedAtUtc"=EXCLUDED."ObservedAtUtc", "ReviewedAtUtc"=EXCLUDED."ReviewedAtUtc",
                  "SourceName"=EXCLUDED."SourceName", "SourceUrl"=EXCLUDED."SourceUrl", "RightsReference"=EXCLUDED."RightsReference",
                  "IsApprovedForDisplay"=true WHERE "IpoGmpQuotes"."ObservedAtUtc" <= EXCLUDED."ObservedAtUtc"
                """, connection, transaction);
            save.Parameters.AddWithValue("id", id); save.Parameters.AddWithValue("premium", entry.GmpMedian!.Value);
            save.Parameters.AddWithValue("low", entry.GmpMin!.Value); save.Parameters.AddWithValue("high", entry.GmpMax!.Value);
            save.Parameters.AddWithValue("count", entry.GmpSources!.Value); save.Parameters.AddWithValue("reported", feed.UpdatedAt.ToUniversalTime());
            save.Parameters.AddWithValue("now", now); save.Parameters.AddWithValue("url", entry.Url);
            save.Parameters.AddWithValue("rights", GmpTodayFeed.RightsReference);
            stored += await save.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return (stored, withheld);
    }
}
