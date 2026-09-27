using Microsoft.Extensions.Configuration;

namespace IPOOnly.Scheduler.Tests;

public sealed class SchedulerConfigurationTests
{
    [Theory]
    [InlineData("open")]
    [InlineData("upcoming")]
    [InlineData("closed")]
    [InlineData("listed")]
    public void StatusBatchDoesNotAppendCodeDefaults(string status)
    {
        var defaults = new[] { "open", "upcoming", "closed", "listed" };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(defaults.Select((value, index) =>
                new KeyValuePair<string, string?>($"Scheduler:Statuses:{index}", value)))
            .AddInMemoryCollection(Enumerable.Range(0, 4).Select(index =>
                new KeyValuePair<string, string?>($"Scheduler:Statuses:{index}", status)))
            .Build();
        var options = new SchedulerOptions();
        configuration.GetSection("Scheduler").Bind(options);

        Assert.Equal(4, options.Statuses.Length);
        Assert.Equal(status, Assert.Single(options.Statuses.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void NormalConfigurationKeepsAllFourStatuses()
    {
        var expected = new[] { "open", "upcoming", "closed", "listed" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            expected.Select((value, index) =>
                new KeyValuePair<string, string?>($"Scheduler:Statuses:{index}", value))).Build();

        Assert.Equal(expected, configuration.GetSection("Scheduler").Get<SchedulerOptions>()!.Statuses);
    }
}
