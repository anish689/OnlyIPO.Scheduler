namespace IPOOnly.Scheduler.Upstox;

public sealed class UpstoxOptions
{
    public const string SectionName = "Upstox";

    public Uri BaseUrl { get; init; } = new("https://api.upstox.com/v2/");
    public string AnalyticsToken { get; init; } = string.Empty;
}
