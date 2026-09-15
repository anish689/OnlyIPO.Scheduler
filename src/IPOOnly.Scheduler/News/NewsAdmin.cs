using System.Text.Json;
using Npgsql;

namespace IPOOnly.Scheduler.News;

// Only available to a local operator with database credentials, never exposed over HTTP.
public static class NewsAdmin
{
    public static async Task RunAsync(NpgsqlDataSource database, string[] args, CancellationToken token)
    {
        if (args.Contains("--news-review"))
        {
            await using var read = database.CreateCommand("""
                SELECT a."Id",s."Name",a."Headline",a."OriginalUrl",a."PublishedAtUtc",a."ContentHash"
                FROM "NewsArticles" a JOIN "NewsSources" s ON s."Id"=a."SourceId"
                WHERE a."Status"='PendingReview' ORDER BY a."FirstSeenAtUtc" DESC LIMIT 100
                """);
            await using var rows = await read.ExecuteReaderAsync(token);
            while (await rows.ReadAsync(token)) Console.WriteLine(JsonSerializer.Serialize(new
            { id = rows.GetInt64(0), source = rows.GetString(1), headline = rows.GetString(2), url = rows.GetString(3), publishedAt = rows.GetDateTime(4), contentHash = rows.GetString(5) }));
            return;
        }
        var publish = args.Contains("--news-publish");
        var key = publish ? "--news-publish" : "--news-withdraw";
        string Value(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }
        if (!long.TryParse(Value(key), out var id) || id < 1 || Value("--note").Length is < 10 or > 1000
            || (publish && Value("--hash").Length != 64))
            throw new ArgumentException("Provide an article id and --note (10-1000 characters). Publishing also requires the reviewed --hash.");
        await using var connection = await database.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = new NpgsqlCommand(publish ? """
            UPDATE "NewsArticles" a SET "Status"='Published',"ReviewNote"=@note,"ReviewedAtUtc"=now()
            FROM "NewsSources" s WHERE a."Id"=@id AND a."SourceId"=s."Id" AND a."ContentHash"=@hash
            AND s."Enabled" AND s."RightsApproved" AND length(trim(s."PermissionReference"))>0
            AND (s."PermissionExpiresAtUtc" IS NULL OR s."PermissionExpiresAtUtc">now())
            AND a."PublishedAtUtc"<=now() AND a."PublishedAtUtc">=now()-s."RetentionDays"*interval '1 day'
            """ : """
            UPDATE "NewsArticles" SET "Status"='Withdrawn',"ReviewNote"=@note,"ReviewedAtUtc"=now() WHERE "Id"=@id
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("note", Value("--note"));
        if (publish) command.Parameters.AddWithValue("hash", Value("--hash"));
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Article missing, changed since review, expired, or source not approved.");
        if (publish && Value("--ipo-slug").Length > 0)
        {
            await using var link = new NpgsqlCommand("""
                INSERT INTO "NewsArticleCompanies" ("ArticleId","IpoId")
                SELECT @id,"Id" FROM ipos WHERE "Slug"=@slug
                ON CONFLICT ("ArticleId","IpoId") DO UPDATE SET "IpoId"=EXCLUDED."IpoId"
                """, connection, transaction);
            link.Parameters.AddWithValue("id", id); link.Parameters.AddWithValue("slug", Value("--ipo-slug"));
            if (await link.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Exact company slug not found; publication cancelled.");
        }
        await using var audit = new NpgsqlCommand("""
            INSERT INTO "NewsReviewEvents" ("Id","ArticleId","Action","Actor","Note","ContentHash","CreatedAtUtc")
            SELECT @event,@id,@action,@actor,@note,"ContentHash",now() FROM "NewsArticles" WHERE "Id"=@id
            """, connection, transaction);
        audit.Parameters.AddWithValue("event", Guid.NewGuid()); audit.Parameters.AddWithValue("id", id);
        audit.Parameters.AddWithValue("action", publish ? "Published" : "Withdrawn");
        audit.Parameters.AddWithValue("actor", Environment.UserName); audit.Parameters.AddWithValue("note", Value("--note"));
        await audit.ExecuteNonQueryAsync(token); await transaction.CommitAsync(token);
        Console.WriteLine(publish ? "Reviewed article published." : "Article withdrawn.");
    }
}
