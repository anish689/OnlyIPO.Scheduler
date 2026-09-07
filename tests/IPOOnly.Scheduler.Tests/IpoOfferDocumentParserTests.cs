using IPOOnly.Scheduler.Documents;

namespace IPOOnly.Scheduler.Tests;

public sealed class IpoOfferDocumentParserTests
{
    [Fact]
    public void Parse_ExtractsValidatedFactsFromOfferDocumentText()
    {
        var parser = new IpoOfferDocumentParser();
        var document = new IpoOfferDocument("RHP", "https://example.com/rhp.pdf", 1);
        var extractedAt = DateTimeOffset.UnixEpoch;

        var facts = parser.Parse(
            document,
            [
                new PdfPageText(
                    42,
                    """
                    The Fresh Issue of Rs. 500.00 crore will be used for funding growth.
                    The Offer for Sale of up to 10,000,000 equity shares is proposed by selling shareholders.
                    Qualified Institutional Buyers shall be allocated not more than 50% of the Net Offer.
                    Non-Institutional Investors shall be allocated not less than 15% of the Net Offer.
                    Retail Individual Investors shall be allocated not less than 35% of the Net Offer.
                    Registrar to the Offer: KFin Technologies Limited.
                    Book Running Lead Managers: ICICI Securities Limited and Axis Capital Limited.
                    """)
            ],
            extractedAt);

        Assert.Contains(facts, fact => fact.FactGroup == "offer-structure" && fact.FactKey == "fresh-issue" && fact.Value == "Rs. 500.00 crore");
        Assert.Contains(facts, fact => fact.FactGroup == "offer-structure" && fact.FactKey == "offer-for-sale" && fact.Value == "up to 10,000,000 equity shares");
        Assert.Contains(facts, fact => fact.FactGroup == "issue-allocation" && fact.FactKey == "qib-allocation" && fact.Value == "50%");
        Assert.Contains(facts, fact => fact.FactGroup == "issue-allocation" && fact.FactKey == "nii-allocation" && fact.Value == "15%");
        Assert.Contains(facts, fact => fact.FactGroup == "issue-allocation" && fact.FactKey == "retail-allocation" && fact.Value == "35%");
        Assert.All(facts, fact =>
        {
            Assert.Equal("RHP", fact.SourceDocumentType);
            Assert.Equal("Available", fact.ValidationStatus);
            Assert.True(fact.ConfidenceScore >= 0.70m);
        });
    }

    [Fact]
    public void Parse_ReturnsNoFactsForUnsupportedText()
    {
        var parser = new IpoOfferDocumentParser();

        var facts = parser.Parse(
            new IpoOfferDocument("RHP", "https://example.com/rhp.pdf", 1),
            [new PdfPageText(1, "This page has a table of contents and no extractable offer facts.")],
            DateTimeOffset.UnixEpoch);

        Assert.Empty(facts);
    }

    [Fact]
    public void Parse_RejectsGenericProseAndTableHeadersForOrganizationFacts()
    {
        var parser = new IpoOfferDocumentParser();

        var facts = parser.Parse(
            new IpoOfferDocument("RHP", "https://example.com/rhp.pdf", 1),
            [
                new PdfPageText(
                    8,
                    """
                    Book Running Lead Managers: These will be based on numerous factors, including factors as described under.
                    Registrar to the Offer: Name and Logo Contact Person Email and Telephone Bigshare Services Private Limited.
                    Registrar to the Issue: Tripartite Agreement dated January 28, 2025 between National Securities Depository Limited, our Company and Registrar to the Issue.
                    The Fresh Issue of 80,000.00 is proposed.
                    Retail Individual Investors subscription was 0.15%.
                    """)
            ],
            DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain(facts, fact => fact.FactGroup is "lead-managers" or "registrar");
        Assert.DoesNotContain(facts, fact => fact.FactKey == "fresh-issue");
        Assert.DoesNotContain(facts, fact => fact.FactKey == "retail-allocation");
    }
}
