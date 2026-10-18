using Npgsql;

namespace IPOOnly.Scheduler;

public static class ProductionConnectionGuard
{
    public static void Validate(string? value)
    {
        NpgsqlConnectionStringBuilder connection;
        try { connection = new NpgsqlConnectionStringBuilder(value); }
        catch (ArgumentException) { throw new InvalidOperationException("Invalid production database configuration."); }

        // Pin this deployment to its isolated pooler identity; never log the connection.
        if (connection.Host != "aws-0-ap-south-1.pooler.supabase.com" ||
            connection.Port != 5432 || connection.Database != "postgres" ||
            connection.Username != "youripo_scheduler.vzbrqhpqawyenwxauztq" ||
            string.IsNullOrWhiteSpace(connection.Password) ||
            connection.SslMode != SslMode.VerifyFull ||
            connection.MaxPoolSize > 3 || connection.MinPoolSize != 0)
            throw new InvalidOperationException("Production database isolation or TLS settings are invalid.");
    }
}
