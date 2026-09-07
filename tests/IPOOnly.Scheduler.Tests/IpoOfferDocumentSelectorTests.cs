using IPOOnly.Scheduler.Documents;
using IPOOnly.Scheduler.Persistence;

namespace IPOOnly.Scheduler.Tests;

public sealed class IpoOfferDocumentSelectorTests
{
    [Fact]
    public void SelectPreferred_PrefersRhpOverDrhp()
    {
        var selector = new IpoOfferDocumentSelector();
        var now = DateTimeOffset.UnixEpoch;

        var selected = selector.SelectPreferred(
        [
            new IpoDocumentRecord("DRHP", "Draft red herring prospectus", "https://example.com/drhp.pdf", "Upstox", now, now, now),
            new IpoDocumentRecord("RHP", "Red herring prospectus", "https://example.com/rhp.pdf", "Upstox", now, now, now)
        ]);

        Assert.NotNull(selected);
        Assert.Equal("RHP", selected.DocumentType);
        Assert.Equal("https://example.com/rhp.pdf", selected.Url);
    }

    [Fact]
    public void SelectPreferred_UsesDrhpWhenRhpIsMissing()
    {
        var selector = new IpoOfferDocumentSelector();
        var now = DateTimeOffset.UnixEpoch;

        var selected = selector.SelectPreferred(
        [
            new IpoDocumentRecord("DRHP", "Draft red herring prospectus", "https://example.com/drhp.pdf", "Upstox", now, now, now)
        ]);

        Assert.NotNull(selected);
        Assert.Equal("DRHP", selected.DocumentType);
    }
}
