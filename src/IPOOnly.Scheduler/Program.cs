using IPOOnly.Scheduler;
using IPOOnly.Scheduler.Documents;
using IPOOnly.Scheduler.Persistence;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using IPOOnly.Scheduler.Tracking;

var builder = Host.CreateApplicationBuilder(args.Where(x => x is not ("--financials-once" or "--financials-preview")).ToArray());

// RSS and review remain independent; only the Upstox news adapter requires its token.
if (args.Any(x => new[] { "--news-once", "--news-worker", "--news-review", "--news-publish", "--news-withdraw", "--news-upstox-check" }.Contains(x)))
{
    builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(
        builder.Configuration.GetConnectionString("IPOOnlyDatabase") ?? throw new InvalidOperationException("Database connection required.")).Build());
    builder.Services.AddSingleton<IPOOnly.Scheduler.News.INewsStore, IPOOnly.Scheduler.News.NewsStore>();
    builder.Services.AddSingleton<IPOOnly.Scheduler.News.NewsFeed>();
    builder.Services.AddSingleton<IPOOnly.Scheduler.News.INewsInstruments, IPOOnly.Scheduler.News.NewsInstruments>();
    builder.Services.AddHttpClient<IPOOnly.Scheduler.News.UpstoxNewsFeed>(client => client.Timeout = Timeout.InfiniteTimeSpan)
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    builder.Services.AddSingleton<IPOOnly.Scheduler.News.INewsFeed, IPOOnly.Scheduler.News.NewsFeedRouter>();
    builder.Services.AddSingleton<IPOOnly.Scheduler.News.NewsWorker>();
    if (args.Contains("--news-worker"))
    {
        builder.Services.AddHostedService(provider => provider.GetRequiredService<IPOOnly.Scheduler.News.NewsWorker>());
        await builder.Build().RunAsync();
    }
    else
    {
        using var host = builder.Build();
        await host.StartAsync();
        if (args.Contains("--news-upstox-check"))
        {
            try
            {
                var source = new IPOOnly.Scheduler.News.NewsSource(Guid.Empty,
                    IPOOnly.Scheduler.News.UpstoxNewsFeed.Endpoint, "upstox.com", false, null, null, 7);
                var batch = await host.Services.GetRequiredService<IPOOnly.Scheduler.News.UpstoxNewsFeed>().ReadAsync(source, CancellationToken.None);
                Console.WriteLine($"Upstox news check: {batch.Entries.Count} unique eligible articles; {batch.Rejected} rejected. Nothing stored or published.");
            }
            catch (Exception error) when (error is HttpRequestException or InvalidDataException or OperationCanceledException)
            {
                Console.WriteLine("Upstox news check failed. Verify token access, response contract and request budget; nothing published.");
                Environment.ExitCode = 1;
            }
        }
        else if (args.Contains("--news-once"))
            Environment.ExitCode = await host.Services.GetRequiredService<IPOOnly.Scheduler.News.NewsWorker>().RunOnceAsync(CancellationToken.None) > 0 ? 1 : 0;
        else
            await IPOOnly.Scheduler.News.NewsAdmin.RunAsync(host.Services.GetRequiredService<NpgsqlDataSource>(), args, CancellationToken.None);
        await host.StopAsync();
    }
    return;
}

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

builder.Services
    .AddOptions<DocumentEnrichmentOptions>()
    .Bind(builder.Configuration.GetSection(DocumentEnrichmentOptions.SectionName))
    .Validate(options => options.MaxDocumentBytes > 0, "Document enrichment byte limit must be positive.")
    .Validate(options => options.DownloadTimeoutSeconds is >= 1 and <= 300, "Document download timeout must be between 1 and 300 seconds.")
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

builder.Services.AddHttpClient<IpoDocumentEnrichmentService>();
builder.Services.AddHttpClient<IPOOnly.Scheduler.Financials.UpstoxFinancialClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); client.MaxResponseContentBufferSize = 2_000_000; })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IPOOnly.Scheduler.Financials.FinancialEnrichmentService>(client => client.Timeout = TimeSpan.FromSeconds(45))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IPOOnly.Scheduler.Financials.FinancialStore>();
builder.Services.AddSingleton<IPOOnly.Scheduler.Financials.RhpFinancialParser>();
builder.Services.AddSingleton<IpoOfferDocumentSelector>();
builder.Services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
builder.Services.AddSingleton<IpoOfferDocumentParser>();
builder.Services.AddSingleton<UpstoxIpoMapper>();
builder.Services.AddSingleton<IpoRepository>();
builder.Services.AddSingleton<IpoSyncService>();
builder.Services.AddOptions<TrackingOptions>().Bind(builder.Configuration.GetSection("Tracking"))
    .Validate(x => x.RecentDays is >= 1 and <= 365 && x.RefreshHours is >= 1 and <= 168, "Tracking date window or interval is invalid.")
    .ValidateOnStart();
builder.Services.AddHttpClient<IMarketDataClient, UpstoxMarketClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<ITrackingStore, TrackingRepository>();
builder.Services.AddSingleton<TrackingSyncService>();
builder.Services.AddSingleton<TrackingWorker>();

if (args.Contains("--financials-once") || args.Contains("--financials-preview"))
{
    using var host = builder.Build();
    await host.StartAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    try
    {
        await IPOOnly.Scheduler.Financials.FinancialCommand.RunAsync(
            host.Services.GetRequiredService<NpgsqlDataSource>(), host.Services.GetRequiredService<IUpstoxIpoClient>(),
            host.Services.GetRequiredService<IPOOnly.Scheduler.Financials.FinancialEnrichmentService>(),
            host.Services.GetRequiredService<IPOOnly.Scheduler.Financials.FinancialStore>(), builder.Configuration,
            args.Contains("--financials-preview"), deadline.Token);
    }
    catch (Exception error)
    {
        Console.WriteLine($"Financial refresh failed ({error.GetType().Name}); stored data retained. Check configuration and source access.");
        Environment.ExitCode = 1;
    }
    await host.StopAsync();
    return;
}

if (args.Any(arg => arg == "--tracking-once"))
{
    using var host = builder.Build();
    await host.StartAsync();
    var result = await host.Services.GetRequiredService<TrackingWorker>().RunOnceAsync(CancellationToken.None);
    if (result?.Failed > 0) Environment.ExitCode = 1;
    await host.StopAsync();
    return;
}

if (args.Any(arg => string.Equals(arg, "--run-once", StringComparison.OrdinalIgnoreCase)))
{
    using var host = builder.Build();
    await host.StartAsync();
    await host.Services.GetRequiredService<IpoSyncService>().SyncAsync(CancellationToken.None);
    await host.StopAsync();
    return;
}

builder.Services.AddHostedService<IpoSyncWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TrackingWorker>());

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
