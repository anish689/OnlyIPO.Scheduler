using Npgsql;

namespace IPOOnly.Scheduler.Tests;

public sealed class ProductionConnectionGuardTests
{
    private const string Valid = "Host=aws-0-ap-south-1.pooler.supabase.com;Port=5432;Database=postgres;Username=youripo_scheduler.vzbrqhpqawyenwxauztq;Password=test-only;SSL Mode=VerifyFull;Maximum Pool Size=3;Minimum Pool Size=0";

    [Fact]
    public void AcceptsIsolatedProductionConnection() => ProductionConnectionGuard.Validate(Valid);

    [Theory]
    [InlineData("Host", "localhost")]
    [InlineData("Port", "6543")]
    [InlineData("Database", "other")]
    [InlineData("Username", "postgres.vzbrqhpqawyenwxauztq")]
    [InlineData("Username", "onlyipo_scheduler.khkmdkyepgktwuqhvqwt")]
    [InlineData("SSL Mode", "Require")]
    [InlineData("Maximum Pool Size", "4")]
    [InlineData("Minimum Pool Size", "1")]
    [InlineData("Password", "")]
    public void RejectsUnsafeConnections(string key, string value)
    {
        var connection = new NpgsqlConnectionStringBuilder(Valid) { [key] = value };
        var error = Assert.Throws<InvalidOperationException>(() => ProductionConnectionGuard.Validate(connection.ConnectionString));
        Assert.DoesNotContain("test-only", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a connection string")]
    public void RejectsMissingOrMalformedConnections(string? value) =>
        Assert.Throws<InvalidOperationException>(() => ProductionConnectionGuard.Validate(value));
}
