using IPOOnly.Scheduler.Persistence;

namespace IPOOnly.Scheduler.Documents;

public sealed class IpoOfferDocumentSelector
{
    public IpoOfferDocument? SelectPreferred(IReadOnlyList<IpoDocumentRecord> documents)
    {
        return documents
            .Select(ToOfferDocument)
            .Where(document => document is not null)
            .OrderBy(document => document!.Priority)
            .FirstOrDefault();
    }

    private static IpoOfferDocument? ToOfferDocument(IpoDocumentRecord document)
    {
        return document.DocumentType.ToUpperInvariant() switch
        {
            "RHP" => new IpoOfferDocument("RHP", document.Url, 1),
            "PROSPECTUS" => new IpoOfferDocument("PROSPECTUS", document.Url, 2),
            "DRHP" => new IpoOfferDocument("DRHP", document.Url, 3),
            _ => null
        };
    }
}
