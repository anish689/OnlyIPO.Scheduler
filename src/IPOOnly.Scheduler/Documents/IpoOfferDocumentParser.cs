using IPOOnly.Scheduler.Persistence;
using System.Text.RegularExpressions;

namespace IPOOnly.Scheduler.Documents;

public sealed class IpoOfferDocumentParser
{
    private const string Available = "Available";
    private const string Method = "RhpFirstPatternParser";

    public IReadOnlyList<IpoDocumentFactRecord> Parse(
        IpoOfferDocument document,
        IReadOnlyList<PdfPageText> pages,
        DateTimeOffset extractedAtUtc)
    {
        var facts = new List<IpoDocumentFactRecord>();

        foreach (var page in pages)
        {
            var text = Normalize(page.Text);
            AddFirstMatch(facts, document, page.PageNumber, text, "offer-structure", "fresh-issue", "Fresh issue", @"fresh\s+issue(?:\s+of)?\s+(?<value>(?:up\s+to\s+)?(?:rs\.?|₹)?\s?[0-9,]+(?:\.[0-9]+)?\s*(?:crore|lakhs?|million|equity\s+shares?)?)", null, 0.78m, extractedAtUtc, IsValidOfferValue);
            AddFirstMatch(facts, document, page.PageNumber, text, "offer-structure", "offer-for-sale", "Offer for sale", @"offer\s+for\s+sale(?:\s+of)?\s+(?<value>(?:up\s+to\s+)?(?:rs\.?|₹)?\s?[0-9,]+(?:\.[0-9]+)?\s*(?:crore|lakhs?|million|equity\s+shares?)?)", null, 0.76m, extractedAtUtc, IsValidOfferValue);
            AddFirstMatch(facts, document, page.PageNumber, text, "registrar", "registrar-to-offer", "Registrar to the offer", @"registrar\s+to\s+the\s+(?:offer|issue)\s*:\s*(?<value>[^.]{5,160})", null, 0.82m, extractedAtUtc, IsValidOrganizationValue);
            AddFirstMatch(facts, document, page.PageNumber, text, "lead-managers", "book-running-lead-managers", "Book running lead managers", @"book\s+running\s+lead\s+managers?\s*:\s*(?<value>[^.]{8,220})", null, 0.74m, extractedAtUtc, IsValidOrganizationValue);
        }

        AddAllocation(facts, document, pages, "qib-allocation", "QIB allocation", @"(?:qualified\s+institutional\s+buyers?|QIBs)\b", extractedAtUtc);
        AddAllocation(facts, document, pages, "nii-allocation", "NII allocation", @"non[-\s]?institutional\s+(?:investors?|bidders?)\b", extractedAtUtc);
        AddAllocation(facts, document, pages, "retail-allocation", "Retail allocation", @"retail\s+(?:individual\s+)?(?:investors?|bidders?)\b", extractedAtUtc);

        return facts
            .GroupBy(fact => new { fact.FactGroup, fact.FactKey })
            .Select(group => group.OrderByDescending(fact => fact.ConfidenceScore).ThenBy(fact => fact.PageNumber).First())
            .Where(fact => fact.ConfidenceScore >= 0.70m)
            .ToList();
    }

    private static void AddAllocation(List<IpoDocumentFactRecord> facts, IpoOfferDocument document,
        IReadOnlyList<PdfPageText> pages, string key, string label, string category, DateTimeOffset at)
    {
        // Explicit grammar prevents a category from consuming the next category's percentage.
        const string value = @"(?<value>(?:(?:not\s+less\s+than|not\s+more\s+than|at\s+least|up\s+to)\s+)?[0-9]+(?:\.[0-9]+)?\s*%)";
        var patterns = new[] {
            category + @"\s+shall\s+be\s+allocated\s+" + value + @"\s+of\s+the\s+net\s+(?:offer|issue)\b",
            value + @"\s+of\s+the\s+net\s+(?:offer|issue)\s+shall\s+be\s+(?:available\s+for\s+allocation|allotted)(?:\s+on\s+a\s+proportionate\s+basis)?\s+to\s+" + category
        };
        var candidates = new List<(string Value, int Page)>();
        foreach (var page in pages)
        foreach (var pattern in patterns)
        foreach (Match match in Regex.Matches(Normalize(page.Text), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var candidate = Regex.Replace(CleanValue(match.Groups["value"].Value), @"\s+%", "%").ToLowerInvariant();
            var number = Regex.Match(candidate, @"[0-9]+(?:\.[0-9]+)?%$").Value;
            if (IsValidAllocationPercentage(number)) candidates.Add((candidate, page.PageNumber));
        }
        // Conflicting statements require review, not an arbitrary first-page winner.
        if (candidates.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) return;
        var selected = candidates.OrderBy(x => x.Page).First();
        facts.Add(new IpoDocumentFactRecord("issue-allocation", key, label, selected.Value, "percentage",
            document.DocumentType, document.Url, selected.Page, 0.78m, Available, "RhpAllocationStatementV2", at, at, at));
    }

    private static void AddFirstMatch(
        List<IpoDocumentFactRecord> facts,
        IpoOfferDocument document,
        int pageNumber,
        string text,
        string group,
        string key,
        string label,
        string pattern,
        string? unit,
        decimal confidence,
        DateTimeOffset extractedAtUtc,
        Func<string, bool>? isValidValue = null)
    {
        if (facts.Any(fact => fact.FactGroup == group && fact.FactKey == key))
        {
            return;
        }

        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return;
        }

        var value = CleanValue(match.Groups["value"].Value);
        if (string.IsNullOrWhiteSpace(value) || isValidValue?.Invoke(value) == false)
        {
            return;
        }

        facts.Add(new IpoDocumentFactRecord(
            group,
            key,
            label,
            value,
            unit,
            document.DocumentType,
            document.Url,
            pageNumber,
            confidence,
            Available,
            Method,
            extractedAtUtc,
            extractedAtUtc,
            extractedAtUtc));
    }

    private static string Normalize(string value)
    {
        return Regex.Replace(value.Replace('\n', ' ').Replace('\r', ' '), @"\s+", " ").Trim();
    }

    private static string CleanValue(string value)
    {
        return Regex.Replace(value, @"\s+", " ").Trim(' ', '.', ',', ':', ';');
    }

    private static bool IsValidAllocationPercentage(string value)
    {
        var numberText = value.Replace("%", string.Empty).Trim();
        return decimal.TryParse(numberText, out var number) && number >= 1m && number <= 100m;
    }

    private static bool IsValidOfferValue(string value)
    {
        return Regex.IsMatch(
            value,
            @"\b(?:rs\.?|crore|lakhs?|million|equity\s+shares?)\b|₹",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsValidOrganizationValue(string value)
    {
        if (value.Length > 180)
        {
            return false;
        }

        var normalized = value.ToLowerInvariant();
        var rejectedFragments = new[]
        {
            "in accordance",
            "basis of",
            "as stated",
            "described under",
            "will be based",
            "these will",
            "including factors",
            "tripartite",
            "agreement",
            "between",
            "our company",
            "contact person",
            "telephone",
            "email",
            "e-mail"
        };

        if (rejectedFragments.Any(normalized.Contains))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"\b(?:limited|private\s+limited|pvt\.?\s*ltd\.?|securities|capital|financial|finserv|technologies|intime|kfin|bigshare|skyline)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
