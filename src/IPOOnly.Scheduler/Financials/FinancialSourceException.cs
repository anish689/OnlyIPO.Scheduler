using System.Net;

namespace IPOOnly.Scheduler.Financials;

public sealed class FinancialSourceException(string sourceKind, HttpStatusCode status)
    : HttpRequestException("Financial source request failed.", null, status)
{
    public string SourceKind { get; } = sourceKind;
}
