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
        Assert.Contains(facts, fact => fact.FactGroup == "issue-allocation" && fact.FactKey == "qib-allocation" && fact.Value == "not more than 50%");
        Assert.Contains(facts, fact => fact.FactGroup == "issue-allocation" && fact.FactKey == "nii-allocation" && fact.Value == "not less than 15%");
        Assert.Contains(facts, fact => fact.FactGroup == "issue-allocation" && fact.FactKey == "retail-allocation" && fact.Value == "not less than 35%");
        Assert.All(facts, fact =>
        {
            Assert.Equal("RHP", fact.SourceDocumentType);
            Assert.Equal("Available", fact.ValidationStatus);
            Assert.True(fact.ConfidenceScore >= 0.70m);
        });
    }

    [Fact]
    public void Parse_PercentageBeforeCategoryDoesNotConsumeNextCategory()
    {
        // Qualiance RHP PDF page 44, reduced to the relevant allocation clauses.
        var facts = ParseText("not less than 15 % of the Net issue shall be available for allocation on a proportionate basis to Non-Institutional Bidders and not less than 35 % of the Net Issue shall be available for allocation on a proportionate basis to Individual Bidders");
        var nii = Assert.Single(facts);
        Assert.Equal("nii-allocation", nii.FactKey);
        Assert.Equal("not less than 15%", nii.Value);
        Assert.Equal(44, nii.PageNumber);
        Assert.Equal("RhpAllocationStatementV2", nii.ExtractionMethod);
    }

    [Theory]
    [InlineData("Non-Institutional Bidders and Retail Individual Investors 35%")]
    [InlineData("Non-Institutional Bidders subscription was 35%")]
    [InlineData("Non-Institutional Bidders. Retail Individual Investors shall be allocated 35% of the Net Issue.")]
    [InlineData("15% of the QIB Portion shall be available for allocation to Non-Institutional Bidders")]
    [InlineData("Non-Institutional Bidders shall be allocated 135% of the Net Issue")]
    public void Parse_RejectsAmbiguousOrInvalidNiiValues(string text)
    {
        Assert.DoesNotContain(ParseText(text), x => x.FactKey == "nii-allocation");
    }

    [Fact]
    public void Parse_WithholdsConflictingAllocationStatements()
    {
        Assert.Empty(ParseText("Non-Institutional Bidders shall be allocated 15% of the Net Issue. Non-Institutional Bidders shall be allocated 25% of the Net Issue."));
    }

    [Fact]
    public void Parse_NormalizesPdfWhitespaceWithoutDroppingQualifier()
    {
        var fact = Assert.Single(ParseText("Non-Institutional\nBidders shall be allocated not less than 15\u00a0% of the Net Issue"));
        Assert.Equal("not less than 15%", fact.Value);
    }

    private static IReadOnlyList<IPOOnly.Scheduler.Persistence.IpoDocumentFactRecord> ParseText(string text) =>
        new IpoOfferDocumentParser().Parse(new IpoOfferDocument("RHP", "https://example.com/rhp.pdf", 1),
            [new PdfPageText(44, text)], DateTimeOffset.UnixEpoch);

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
