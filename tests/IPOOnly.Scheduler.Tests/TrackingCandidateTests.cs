using IPOOnly.Scheduler.Tracking;

namespace IPOOnly.Scheduler.Tests;

public sealed class TrackingCandidateTests
{
    [Fact]
    public void RestrictedCandidatesNeverReferencePrivateWatchlists()
    {
        var sql = TrackingRepository.CandidateSql(false);
        Assert.DoesNotContain("WatchlistItems", sql);
        Assert.Contains(">= @since", sql);
        Assert.Contains("< @today", sql);
        Assert.Contains("<> 'Withdrawn'", sql);
    }

    [Fact]
    public void ExistingFullAccessModeRetainsOlderSavedCompanies()
    {
        Assert.True(new TrackingOptions().IncludeWatchlisted);
        Assert.Contains("OR EXISTS (SELECT 1 FROM \"WatchlistItems\"", TrackingRepository.CandidateSql(true));
    }
}
