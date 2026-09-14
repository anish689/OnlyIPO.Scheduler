using Npgsql;

namespace IPOOnly.Scheduler.Financials;

public sealed class FinancialStore(NpgsqlDataSource dataSource)
{
    public async Task<bool> IsFreshAsync(Guid ipoId, CancellationToken token)
    {
        await using var query = dataSource.CreateCommand("SELECT MAX(\"RetrievedAtUtc\") FROM \"IpoFinancials\" WHERE \"IpoId\" = @id");
        query.Parameters.AddWithValue("id", ipoId);
        return await query.ExecuteScalarAsync(token) is DateTime retrieved && retrieved >= DateTime.UtcNow.AddHours(-24);
    }

    public async Task SaveAsync(Guid ipoId, IReadOnlyList<FinancialObservation> rows, CancellationToken token)
    {
        if (!FinancialValidation.Usable(rows)) return;
        await using var connection = await dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        // Serialize concurrent enrichment for the same IPO before replacing its snapshot.
        await using (var gate = new NpgsqlCommand("SELECT \"Id\" FROM ipos WHERE \"Id\" = @id FOR UPDATE", connection, transaction))
        {
            gate.Parameters.AddWithValue("id", ipoId);
            if (await gate.ExecuteScalarAsync(token) is null) throw new InvalidDataException("Unknown IPO.");
        }
        // One coherent validated snapshot replaces the previous set atomically. Empty/error fetches never reach here.
        await using (var remove = new NpgsqlCommand("DELETE FROM \"IpoFinancials\" WHERE \"IpoId\" = @id", connection, transaction))
        {
            remove.Parameters.AddWithValue("id", ipoId);
            await remove.ExecuteNonQueryAsync(token);
        }
        foreach (var row in rows.DistinctBy(x => (x.Metric, x.PeriodEnd)))
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO "IpoFinancials" ("Id", "IpoId", "Metric", "PeriodEnd", "Basis", "Frequency", "Value", "Currency", "Unit", "Source", "SourceUrl", "PageNumber", "RetrievedAtUtc")
                VALUES (@id,@ipo,@metric,@period,@basis,@frequency,@value,@currency,@unit,@source,@url,@page,@at)
                """, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("ipo", ipoId);
            command.Parameters.AddWithValue("metric", row.Metric); command.Parameters.AddWithValue("period", row.PeriodEnd);
            command.Parameters.AddWithValue("basis", row.Basis); command.Parameters.AddWithValue("frequency", row.Frequency);
            command.Parameters.AddWithValue("value", row.Value); command.Parameters.AddWithValue("currency", row.Currency);
            command.Parameters.AddWithValue("unit", row.Unit); command.Parameters.AddWithValue("source", row.Source);
            command.Parameters.AddWithValue("url", row.SourceUrl);
            command.Parameters.AddWithValue("page", NpgsqlTypes.NpgsqlDbType.Integer, (object?)row.PageNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("at", row.RetrievedAtUtc);
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }
}
