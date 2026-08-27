namespace IPOOnly.Scheduler;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    public string[] Statuses { get; init; } = ["open", "upcoming", "closed", "listed"];
    public int PageSize { get; init; } = 30;
    public int SyncIntervalMinutes { get; init; } = 10;
    public int JitterMaxSeconds { get; init; } = 45;
    public bool RunOnStartup { get; init; } = true;
}
