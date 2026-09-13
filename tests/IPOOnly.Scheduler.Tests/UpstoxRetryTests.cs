using System.Net;
using IPOOnly.Scheduler.Upstox;
using Microsoft.Extensions.Options;

namespace IPOOnly.Scheduler.Tests;

public sealed class UpstoxRetryTests
{
    [Fact]
    public async Task RecoversAfterTransientConnectionReset()
    {
        var handler = new TestHandler(null);
        await Client(handler).GetIpoPageAsync("open", 1, 30, CancellationToken.None);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task DoesNotRetryInvalidCredentials()
    {
        var handler = new TestHandler(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).GetIpoPageAsync("open", 1, 30, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task StopsAfterThreeServerFailures()
    {
        var handler = new TestHandler(HttpStatusCode.ServiceUnavailable, true);
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).GetIpoPageAsync("open", 1, 30, CancellationToken.None));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task CancellationStopsRetryBackoff()
    {
        var handler = new TestHandler(null);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).GetIpoPageAsync("open", 1, 30, cancellation.Token));
        Assert.Equal(1, handler.Calls);
    }

    private static UpstoxIpoClient Client(TestHandler handler) => new(new HttpClient(handler)
        { BaseAddress = new Uri("https://example.com/") }, Options.Create(new UpstoxOptions { AnalyticsToken = "test" }));

    private sealed class TestHandler(HttpStatusCode? status, bool alwaysFail = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1 || alwaysFail) throw new HttpRequestException("Simulated transient failure", null, status);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[]}") });
        }
    }
}
