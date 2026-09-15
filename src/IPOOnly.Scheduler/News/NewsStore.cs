using Npgsql;
using NpgsqlTypes;

namespace IPOOnly.Scheduler.News;

public interface INewsStore
{
    Task<IReadOnlyList<NewsSource>> ClaimAsync(Guid owner, CancellationToken token);
    Task CompleteAsync(NewsSource source, Guid owner, NewsBatch? batch, string outcome, CancellationToken token);
}

public sealed class NewsStore(NpgsqlDataSource database) : INewsStore
{
    public async Task<IReadOnlyList<NewsSource>> ClaimAsync(Guid owner, CancellationToken token)
    {
        await using (var purge = database.CreateCommand("""
            DELETE FROM "NewsArticles" a USING "NewsSources" s WHERE a."SourceId"=s."Id"
              AND a."PublishedAtUtc" < now()-greatest(1,least(90,s."RetentionDays"))*interval '1 day';
            UPDATE "NewsArticles" a SET "Description"=NULL FROM "NewsSources" s
              WHERE a."SourceId"=s."Id" AND NOT s."DescriptionAllowed" AND a."Description" IS NOT NULL;
            DELETE FROM "NewsIngestionRuns" WHERE "CompletedAtUtc"<now()-interval '90 days';
            """)) await purge.ExecuteNonQueryAsync(token);
        await using var command = database.CreateCommand("""
            WITH due AS (
                SELECT "Id" FROM "NewsSources"
                WHERE "Enabled" AND "RightsApproved" AND length(trim("PermissionReference")) > 0
                AND ("PermissionExpiresAtUtc" IS NULL OR "PermissionExpiresAtUtc" > now())
                AND "RetentionDays" BETWEEN 1 AND 90 AND "PollMinutes" BETWEEN 15 AND 1440
                AND ("LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" < now())
                AND ("LastAttemptAtUtc" IS NULL OR "LastAttemptAtUtc" < now() - "PollMinutes" * interval '1 minute')
                ORDER BY "LastAttemptAtUtc" NULLS FIRST LIMIT 2 FOR UPDATE SKIP LOCKED
            )
            UPDATE "NewsSources" s SET "LeaseOwner"=@owner, "LeaseUntilUtc"=now()+interval '2 minutes', "LastAttemptAtUtc"=now()
            FROM due WHERE s."Id"=due."Id"
            RETURNING s."Id", s."FeedUrl", s."ArticleHost", s."DescriptionAllowed", s."ETag", s."LastModifiedUtc", s."RetentionDays"
            """);
        command.Parameters.AddWithValue("owner", owner);
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<NewsSource>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5), reader.GetInt32(6)));
        return rows;
    }

    public async Task CompleteAsync(NewsSource source, Guid owner, NewsBatch? batch, string outcome, CancellationToken token)
    {
        await using var connection = await database.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var gate = new NpgsqlCommand("""
            SELECT "DescriptionAllowed" FROM "NewsSources" WHERE "Id"=@id AND "LeaseOwner"=@owner
            AND "LeaseUntilUtc">now() AND "Enabled" AND "RightsApproved" AND length(trim("PermissionReference"))>0
            AND ("PermissionExpiresAtUtc" IS NULL OR "PermissionExpiresAtUtc">now()) FOR UPDATE
            """, connection, transaction);
        gate.Parameters.AddWithValue("id", source.Id); gate.Parameters.AddWithValue("owner", owner);
        if (await gate.ExecuteScalarAsync(token) is not bool descriptionAllowed) return;
        var now = DateTime.UtcNow;
        if (batch != null)
        {
            foreach (var entry in batch.Entries)
            {
                // A correction returns to review; it must not silently replace a published assertion.
                await using var save = new NpgsqlCommand("""
                    INSERT INTO "NewsArticles" ("SourceId","ExternalIdHash","UrlHash","OriginalUrl","Headline","Description",
                        "ContentHash","PublishedAtUtc","FirstSeenAtUtc","LastFetchedAtUtc","Status")
                    SELECT @source,@external,@urlhash,@url,@headline,@description,@hash,@published,@now,@now,'PendingReview'
                    WHERE NOT EXISTS (SELECT 1 FROM "NewsArticles" WHERE "SourceId"=@source AND "UrlHash"=@urlhash AND "ExternalIdHash"<>@external)
                    ON CONFLICT ("SourceId","ExternalIdHash") DO UPDATE SET
                        "LastFetchedAtUtc"=@now, "Headline"=@headline, "Description"=@description,
                        "OriginalUrl"=@url, "UrlHash"=@urlhash, "ContentHash"=@hash,
                        "UpdatedAtUtc"=CASE WHEN "NewsArticles"."ContentHash"<>@hash THEN @now ELSE "NewsArticles"."UpdatedAtUtc" END,
                        "Status"=CASE WHEN "NewsArticles"."ContentHash"<>@hash THEN 'PendingReview' ELSE "NewsArticles"."Status" END
                    """, connection, transaction);
                save.Parameters.AddWithValue("source", source.Id);
                save.Parameters.AddWithValue("external", entry.ExternalIdHash); save.Parameters.AddWithValue("urlhash", entry.UrlHash);
                save.Parameters.AddWithValue("url", entry.Url); save.Parameters.AddWithValue("headline", entry.Headline);
                save.Parameters.AddWithValue("description", NpgsqlDbType.Text, (object?)(descriptionAllowed ? entry.Description : null) ?? DBNull.Value);
                save.Parameters.AddWithValue("hash", entry.ContentHash); save.Parameters.AddWithValue("published", entry.PublishedAtUtc);
                save.Parameters.AddWithValue("now", now); await save.ExecuteNonQueryAsync(token);
            }
        }
        await using var finish = new NpgsqlCommand("""
            UPDATE "NewsSources" SET "LeaseUntilUtc"=NULL,"LeaseOwner"=NULL,
                "LastSuccessAtUtc"=CASE WHEN @success THEN @now ELSE "LastSuccessAtUtc" END,
                "ETag"=CASE WHEN @success THEN @etag ELSE "ETag" END,
                "LastModifiedUtc"=CASE WHEN @success THEN @modified ELSE "LastModifiedUtc" END WHERE "Id"=@id;
            INSERT INTO "NewsIngestionRuns" ("Id","SourceId","StartedAtUtc","CompletedAtUtc","Outcome","Accepted","Rejected")
                SELECT @run,@id,"LastAttemptAtUtc",@now,@outcome,@accepted,@rejected FROM "NewsSources" WHERE "Id"=@id;
            DELETE FROM "NewsArticles" WHERE "SourceId"=@id AND "PublishedAtUtc"<@now-@days*interval '1 day';
            DELETE FROM "NewsIngestionRuns" WHERE "SourceId"=@id AND "CompletedAtUtc"<@now-interval '90 days';
            """, connection, transaction);
        finish.Parameters.AddWithValue("id", source.Id); finish.Parameters.AddWithValue("success", batch != null);
        finish.Parameters.AddWithValue("now", now); finish.Parameters.AddWithValue("run", Guid.NewGuid());
        finish.Parameters.AddWithValue("outcome", outcome); finish.Parameters.AddWithValue("accepted", batch?.Entries.Count ?? 0);
        finish.Parameters.AddWithValue("rejected", batch?.Rejected ?? 0); finish.Parameters.AddWithValue("days", source.RetentionDays);
        finish.Parameters.AddWithValue("etag", NpgsqlDbType.Text, (object?)batch?.ETag ?? DBNull.Value);
        finish.Parameters.AddWithValue("modified", NpgsqlDbType.TimestampTz, (object?)batch?.LastModifiedUtc ?? DBNull.Value);
        await finish.ExecuteNonQueryAsync(token); await transaction.CommitAsync(token);
    }
}
