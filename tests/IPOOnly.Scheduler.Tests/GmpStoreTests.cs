using IPOOnly.Scheduler.Gmp;
using Npgsql;

namespace IPOOnly.Scheduler.Tests;

public sealed class GmpStoreTests
{
    [PostgreSqlFact]
    public async Task Refresh_is_atomic_idempotent_and_isolated_from_application_reads()
    {
        var schema = "gmp_test_" + Guid.NewGuid().ToString("N");
        await using var admin = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ONLYIPO_TEST_DATABASE")!);
        await using (var create = admin.CreateCommand($"CREATE SCHEMA {schema}")) await create.ExecuteNonQueryAsync();
        try
        {
            var options = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { SearchPath = schema, CommandTimeout = 5 };
            await using var db = NpgsqlDataSource.Create(options.ConnectionString);
            await using (var setup = db.CreateCommand("""
                CREATE TABLE ipos ("Id" uuid PRIMARY KEY, "Slug" text UNIQUE, "Status" text,
                  "OpenDate" timestamptz, "ListingDate" timestamptz, "PriceBandMaximum" numeric);
                CREATE TABLE watchlist (ipo_id uuid REFERENCES ipos("Id"));
                CREATE TABLE "IpoGmpQuotes" ("IpoId" uuid PRIMARY KEY REFERENCES ipos("Id"),
                  "Premium" numeric(18,2), "RangeLow" numeric(18,2), "RangeHigh" numeric(18,2),
                  "SourceCount" int, "ObservedAtUtc" timestamptz, "ReviewedAtUtc" timestamptz,
                  "SourceName" text, "SourceUrl" varchar(1000), "RightsReference" text, "IsApprovedForDisplay" boolean);
                """)) await setup.ExecuteNonQueryAsync();
            var now = DateTimeOffset.Parse("2026-11-18T10:00:00Z");
            var id = Guid.NewGuid();
            await using (var seed = db.CreateCommand("""
                INSERT INTO ipos VALUES (@id,'example-ipo','Open','2026-11-18T00:00:00Z','2026-11-25T00:00:00Z',100)
                """))
            {
                seed.Parameters.AddWithValue("id", id);
                await seed.ExecuteNonQueryAsync();
            }
            GmpMapping[] mappings = [new("example-ipo", "example")];
            var entry = new GmpEntry("example", "https://gmptoday.in/ipo/example/", "Open", new(2026, 11, 18), 100, -5, -5, -5, 4);
            var feed = new GmpFeed(now.AddHours(-1), "https://gmptoday.in", "CC-BY-4.0", true, [entry]);
            Task<(int Stored, int Withheld)> Save(GmpFeed value) => GmpCommand.SaveAsync(db, mappings, value, now, default);
            async Task<(decimal Premium, bool Approved, DateTimeOffset Observed)> Read()
            {
                await using var query = db.CreateCommand("SELECT \"Premium\",\"IsApprovedForDisplay\",\"ObservedAtUtc\" FROM \"IpoGmpQuotes\"");
                await using var reader = await query.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var value = (reader.GetDecimal(0), reader.GetBoolean(1), reader.GetFieldValue<DateTimeOffset>(2));
                Assert.False(await reader.ReadAsync());
                return value;
            }
            Assert.Equal((1, 0), await Save(feed));
            Assert.Equal((1, 0), await Save(feed));
            Assert.Equal((-5m, true, feed.UpdatedAt), await Read());

            // A rotating sample may omit an IPO. Keep its original timestamp, never extend expiry.
            Assert.Equal((0, 1), await Save(feed with { Ipos = [] }));
            Assert.Equal(feed.UpdatedAt, (await Read()).Observed);
            Assert.Equal((0, 0), await Save(feed with { UpdatedAt = feed.UpdatedAt.AddMinutes(-1), Ipos = [entry with { GmpMedian = 0, GmpMin = 0, GmpMax = 0 }] }));
            Assert.Equal(-5m, (await Read()).Premium);

            Assert.Equal((0, 1), await Save(feed with { Ipos = [entry with { PriceBand = 101 }] }));
            Assert.False((await Read()).Approved);
            await Save(feed);

            await using var user = await db.OpenConnectionAsync();
            await using (var transaction = await user.BeginTransactionAsync())
            {
                await using var watch = new NpgsqlCommand("INSERT INTO watchlist VALUES (@id)", user, transaction);
                watch.Parameters.AddWithValue("id", id);
                await watch.ExecuteNonQueryAsync();
                Assert.Equal((1, 0), await Save(feed));
                await transaction.RollbackAsync();
            }
            await using (var transaction = await user.BeginTransactionAsync())
            {
                await using var lockQuote = new NpgsqlCommand("SELECT \"IpoId\" FROM \"IpoGmpQuotes\" FOR UPDATE", user, transaction);
                await lockQuote.ExecuteScalarAsync();
                var pending = Save(feed);
                Assert.Equal(-5m, (await Read()).Premium);
                var error = await Assert.ThrowsAsync<PostgresException>(() => pending);
                Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
                await transaction.RollbackAsync();
            }
            await using (var transaction = await user.BeginTransactionAsync())
            {
                await using var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(1800261118)", user, transaction);
                await gate.ExecuteNonQueryAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(() => Save(feed));
                await transaction.RollbackAsync();
            }

            // A later row failing must roll back earlier mutations in the same batch.
            var longSlug = new string('a', 1000);
            await using (var seed = db.CreateCommand("""
                INSERT INTO ipos VALUES (@id,'second-ipo','Open','2026-11-18T00:00:00Z',NULL,100)
                """))
            {
                seed.Parameters.AddWithValue("id", Guid.NewGuid());
                await seed.ExecuteNonQueryAsync();
            }
            GmpMapping[] batch = [mappings[0], new("second-ipo", longSlug)];
            var badBatch = feed with { Ipos = [entry with { PriceBand = 101 }, entry with { Slug = longSlug, Url = $"https://gmptoday.in/ipo/{longSlug}/" }] };
            await Assert.ThrowsAsync<PostgresException>(() => GmpCommand.SaveAsync(db, batch, badBatch, now, default));
            Assert.True((await Read()).Approved);
        }
        finally
        {
            await using var drop = admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");
            await drop.ExecuteNonQueryAsync();
        }
    }
}
