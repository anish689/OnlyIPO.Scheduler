namespace IPOOnly.Scheduler.Documents;

public sealed class DocumentEnrichmentOptions
{
    public const string SectionName = "DocumentEnrichment";

    public bool Enabled { get; set; } = true;
    public int MaxDocumentBytes { get; set; } = 20 * 1024 * 1024;
}
