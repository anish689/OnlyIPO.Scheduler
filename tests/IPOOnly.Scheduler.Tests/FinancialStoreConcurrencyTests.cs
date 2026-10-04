using IPOOnly.Scheduler.Financials;
using Npgsql;

namespace IPOOnly.Scheduler.Tests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ONLYIPO_TEST_DATABASE")))
            Skip = "Set ONLYIPO_TEST_DATABASE to an isolated local/CI PostgreSQL database.";
    }
}

public sealed class FinancialStoreConcurrencyTests
{
    [PostgreSqlFact]
    public async Task Refresh_preserves_reads_and_foreign_key_writes_and_rolls_back_on_contention()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONLYIPO_TEST_DATABASE")!;
        var schema = "financial_test_" + Guid.NewGuid().ToString("N");
        await using var admin = NpgsqlDataSource.Create(connectionString);
        await using (var create = admin.CreateCommand($"CREATE SCHEMA {schema}")) await create.ExecuteNonQueryAsync();
        try
        {
            var options = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, CommandTimeout = 5 };
            await using var db = NpgsqlDataSource.Create(options.ConnectionString);
            await using (var setup = db.CreateCommand("""
                CREATE TABLE ipos ("Id" uuid PRIMARY KEY);
                CREATE TABLE watchlist (ipo_id uuid REFERENCES ipos("Id"));
                CREATE TABLE activity (id integer);
                CREATE TABLE "IpoFinancials" (
                    "Id" uuid PRIMARY KEY, "IpoId" uuid REFERENCES ipos("Id"),
                    "Metric" text, "PeriodEnd" date, "Basis" text, "Frequency" text,
                    "Value" numeric CHECK ("Value" >= 0), "Currency" text, "Unit" text,
                    "Source" text, "SourceUrl" text, "PageNumber" integer, "RetrievedAtUtc" timestamptz);
                """)) await setup.ExecuteNonQueryAsync();
            var id = Guid.NewGuid();
            await using (var seed = db.CreateCommand("INSERT INTO ipos VALUES (@id)"))
            {
                seed.Parameters.AddWithValue("id", id);
                await seed.ExecuteNonQueryAsync();
            }
            var store = new FinancialStore(db);
            FinancialObservation[] Rows(decimal value) => new[] { 2023, 2024 }.Select(year =>
                new FinancialObservation("revenue", new(year, 3, 31), "standalone", "yearly", value,
                    "INR", "crore", "Upstox", "https://example.com/financials", null, DateTimeOffset.UtcNow)).ToArray();
            async Task<decimal> Sum()
            {
                await using var read = db.CreateCommand("SELECT SUM(\"Value\") FROM \"IpoFinancials\"");
                return (decimal)(await read.ExecuteScalarAsync())!;
            }
            await store.SaveAsync(id, Rows(10), CancellationToken.None);

            // A user's in-flight watchlist insert holds KEY SHARE on the parent IPO.
            await using var userConnection = await db.OpenConnectionAsync();
            await using (var userTransaction = await userConnection.BeginTransactionAsync())
            {
                await using var watch = new NpgsqlCommand("INSERT INTO watchlist VALUES (@id)", userConnection, userTransaction);
                watch.Parameters.AddWithValue("id", id);
                await watch.ExecuteNonQueryAsync();
                await store.SaveAsync(id, Rows(20), CancellationToken.None);
                Assert.Equal(40m, await Sum());
                await userTransaction.CommitAsync();
            }

            // Contended refresh waits only briefly; API-style reads/writes still succeed.
            await using (var blocker = await userConnection.BeginTransactionAsync())
            {
                await using var gate = new NpgsqlCommand("SELECT \"Id\" FROM ipos FOR UPDATE", userConnection, blocker);
                await gate.ExecuteScalarAsync();
                var refresh = store.SaveAsync(id, Rows(30), CancellationToken.None);
                Assert.Equal(40m, await Sum());
                await using var activity = db.CreateCommand("INSERT INTO activity VALUES (1)");
                await activity.ExecuteNonQueryAsync();
                var error = await Assert.ThrowsAsync<PostgresException>(() => refresh);
                Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
                await blocker.RollbackAsync();
            }
            Assert.Equal(40m, await Sum());
            // Failure after DELETE must roll back the whole replacement, not publish an empty set.
            await Assert.ThrowsAsync<PostgresException>(() => store.SaveAsync(id, Rows(-1), CancellationToken.None));
            Assert.Equal(40m, await Sum());
        }
        finally
        {
            await using var drop = admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");
            await drop.ExecuteNonQueryAsync();
        }
    }
}
