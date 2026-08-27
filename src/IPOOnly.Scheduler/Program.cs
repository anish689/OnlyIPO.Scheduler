using IPOOnly.Scheduler;
using IPOOnly.Scheduler.Persistence;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<UpstoxOptions>()
    .Bind(builder.Configuration.GetSection(UpstoxOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.AnalyticsToken), "Upstox token is required.")
    .ValidateOnStart();

builder.Services
    .AddOptions<SchedulerOptions>()
    .Bind(builder.Configuration.GetSection(SchedulerOptions.SectionName))
    .Validate(options => options.PageSize is > 0 and <= 30, "Page size must be between 1 and 30.")
    .Validate(options => options.SyncIntervalMinutes > 0, "Sync interval must be positive.")
    .ValidateOnStart();

builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("IPOOnlyDatabase");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:IPOOnlyDatabase is required.");
    }

    return new NpgsqlDataSourceBuilder(connectionString).Build();
});

builder.Services.AddHttpClient<IUpstoxIpoClient, UpstoxIpoClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<UpstoxOptions>>().Value;
    client.BaseAddress = options.BaseUrl;
});

builder.Services.AddSingleton<UpstoxIpoMapper>();
builder.Services.AddSingleton<IpoRepository>();
builder.Services.AddSingleton<IpoSyncService>();

if (args.Any(arg => string.Equals(arg, "--run-once", StringComparison.OrdinalIgnoreCase)))
{
    using var host = builder.Build();
    await host.StartAsync();
    await host.Services.GetRequiredService<IpoSyncService>().SyncAsync(CancellationToken.None);
    await host.StopAsync();
    return;
}

builder.Services.AddHostedService<IpoSyncWorker>();

await builder.Build().RunAsync();

public sealed class IpoSyncWorker(
    IpoSyncService syncService,
    IOptions<SchedulerOptions> options,
    ILogger<IpoSyncWorker> logger) : BackgroundService
{
    private readonly Random _jitter = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.RunOnStartup)
        {
            await RunSafelyAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMinutes(options.Value.SyncIntervalMinutes)
                .Add(TimeSpan.FromSeconds(_jitter.Next(0, options.Value.JitterMaxSeconds + 1)));

            await Task.Delay(delay, stoppingToken);
            await RunSafelyAsync(stoppingToken);
        }
    }

    private async Task RunSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await syncService.SyncAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "IPO sync failed. Last-known-good database data remains available.");
        }
    }
}
