using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace IPOOnly.Scheduler.Tracking;

public interface ITrackingStore
{
    Task<IReadOnlyList<TrackingCandidate>> GetCandidatesAsync(DateOnly today, int recentDays, CancellationToken token);
    Task<Guid> MapAsync(TrackingCandidate ipo, EquityInstrument instrument, CancellationToken token);
    Task SaveAsync(Guid ipoId, Guid instrumentId, CandleBatch batch, CancellationToken token);
}

public sealed class TrackingRepository(NpgsqlDataSource source) : ITrackingStore
{
    public async Task<IReadOnlyList<TrackingCandidate>> GetCandidatesAsync(DateOnly today, int recentDays, CancellationToken token)
    {
        const string sql = """
            WITH identities AS (
                SELECT DISTINCT ON (item->>'id') item->>'id' AS slug,
                    NULLIF(item->>'isin', '') AS isin, NULLIF(item->>'symbol', '') AS symbol
                FROM "IpoSourceSnapshots" s
                CROSS JOIN LATERAL jsonb_array_elements(
                    CASE WHEN jsonb_typeof(s."PayloadJson"::jsonb->'data') = 'array'
                         THEN s."PayloadJson"::jsonb->'data' ELSE '[]'::jsonb END) item
                WHERE s."SourceName" = 'Upstox'
                ORDER BY item->>'id', s."CapturedAtUtc" DESC
            )
            SELECT i."Id", i."Slug", (i."ListingDate" AT TIME ZONE 'Asia/Kolkata')::date,
                i."Exchanges", COALESCE(m."Isin", d.isin), COALESCE(m."Symbol", d.symbol),
                m."Id", m."InstrumentKey", (SELECT max(p."TradeDate") FROM "ListedPriceSnapshots" p WHERE p."ListedInstrumentId" = m."Id")
            FROM ipos i LEFT JOIN identities d ON d.slug = i."Slug"
            LEFT JOIN "ListedInstruments" m ON m."IpoId" = i."Id"
            WHERE i."SourceName" = 'Upstox' AND i."Status" <> 'Withdrawn'
                AND (i."ListingDate" AT TIME ZONE 'Asia/Kolkata')::date < @today
                AND ((i."ListingDate" AT TIME ZONE 'Asia/Kolkata')::date >= @since
                    OR EXISTS (SELECT 1 FROM "WatchlistItems" w WHERE w."IpoId" = i."Id"))
            ORDER BY i."ListingDate" DESC
            """;
        await using var command = source.CreateCommand(sql);
        command.Parameters.AddWithValue("today", today);
        command.Parameters.AddWithValue("since", today.AddDays(-recentDays));
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<TrackingCandidate>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateOnly>(2), reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8)));
        return rows;
    }

    public async Task<Guid> MapAsync(TrackingCandidate ipo, EquityInstrument instrument, CancellationToken token)
    {
        const string sql = """
            INSERT INTO "ListedInstruments" ("Id", "IpoId", "Exchange", "Symbol", "Isin", "InstrumentKey", "MappingBasis", "SourceName", "MappedAtUtc")
            VALUES (@id, @ipo, @exchange, @symbol, @isin, @key, @basis, 'Upstox', @now)
            ON CONFLICT ("IpoId") DO UPDATE SET "Symbol" = EXCLUDED."Symbol"
            WHERE "ListedInstruments"."InstrumentKey" = EXCLUDED."InstrumentKey"
            RETURNING "Id"
            """;
        await using var command = source.CreateCommand(sql);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("ipo", ipo.IpoId);
        command.Parameters.AddWithValue("exchange", instrument.Exchange);
        command.Parameters.AddWithValue("symbol", instrument.Symbol);
        command.Parameters.AddWithValue("isin", instrument.Isin);
        command.Parameters.AddWithValue("key", instrument.Key);
        command.Parameters.AddWithValue("basis", string.IsNullOrWhiteSpace(ipo.Isin) ? "SymbolExchange" : "IsinExchange");
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        return await command.ExecuteScalarAsync(token) is Guid id ? id : throw new InvalidDataException("Instrument identity changed; review required.");
    }

    public async Task SaveAsync(Guid ipoId, Guid instrumentId, CandleBatch batch, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var retrieved = DateTimeOffset.UtcNow;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(batch.RawJson)));
        await using var snapshot = new NpgsqlCommand("""
            INSERT INTO "IpoSourceSnapshots" ("Id", "IpoId", "SourceName", "SourceRecordId", "SourceEndpoint", "SourceStatus", "PayloadJson", "PayloadHash", "CapturedAtUtc", "SourceUpdatedAt")
            VALUES (@id, @ipo, 'Upstox', @record, @endpoint, 'Validated', @json, @hash, @now, NULL)
            ON CONFLICT ("SourceName", "SourceRecordId", "SourceEndpoint", "PayloadHash")
            DO UPDATE SET "CapturedAtUtc" = EXCLUDED."CapturedAtUtc" RETURNING "Id"
            """, connection, transaction);
        snapshot.Parameters.AddWithValue("id", Guid.NewGuid());
        snapshot.Parameters.AddWithValue("ipo", ipoId);
        snapshot.Parameters.AddWithValue("record", "market:" + instrumentId);
        snapshot.Parameters.AddWithValue("endpoint", batch.Endpoint);
        snapshot.Parameters.AddWithValue("json", NpgsqlDbType.Jsonb, batch.RawJson);
        snapshot.Parameters.AddWithValue("hash", hash);
        snapshot.Parameters.AddWithValue("now", retrieved);
        var sourceId = (Guid)(await snapshot.ExecuteScalarAsync(token))!;
        foreach (var candle in batch.Candles)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO "ListedPriceSnapshots" ("Id", "ListedInstrumentId", "TradeDate", "Open", "High", "Low", "Close", "Volume", "SourceSnapshotId", "RetrievedAtUtc")
                VALUES (@id, @instrument, @date, @open, @high, @low, @close, @volume, @source, @now)
                ON CONFLICT ("ListedInstrumentId", "TradeDate") DO UPDATE SET
                    "Open" = EXCLUDED."Open", "High" = EXCLUDED."High", "Low" = EXCLUDED."Low", "Close" = EXCLUDED."Close",
                    "Volume" = EXCLUDED."Volume", "SourceSnapshotId" = EXCLUDED."SourceSnapshotId", "RetrievedAtUtc" = EXCLUDED."RetrievedAtUtc"
                """, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("instrument", instrumentId);
            command.Parameters.AddWithValue("date", candle.Date);
            command.Parameters.AddWithValue("open", candle.Open);
            command.Parameters.AddWithValue("high", candle.High);
            command.Parameters.AddWithValue("low", candle.Low);
            command.Parameters.AddWithValue("close", candle.Close);
            command.Parameters.AddWithValue("volume", candle.Volume);
            command.Parameters.AddWithValue("source", sourceId);
            command.Parameters.AddWithValue("now", retrieved);
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }
}
