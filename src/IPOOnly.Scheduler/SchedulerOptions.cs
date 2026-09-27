namespace IPOOnly.Scheduler;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    // Defaults live in appsettings.json; binding appends to a prepopulated array.
    public string[] Statuses { get; init; } = [];
    public int PageSize { get; init; } = 30;
    public int SyncIntervalMinutes { get; init; } = 10;
    public int JitterMaxSeconds { get; init; } = 45;
    public bool RunOnStartup { get; init; } = true;
}
