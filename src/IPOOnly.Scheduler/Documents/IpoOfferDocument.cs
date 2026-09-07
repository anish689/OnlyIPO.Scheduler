namespace IPOOnly.Scheduler.Documents;

public sealed record IpoOfferDocument(
    string DocumentType,
    string Url,
    int Priority);
