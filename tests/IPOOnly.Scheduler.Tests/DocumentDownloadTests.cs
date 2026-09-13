using System.Net;
using IPOOnly.Scheduler.Documents;
using IPOOnly.Scheduler.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IPOOnly.Scheduler.Tests;

public sealed class DocumentDownloadTests
{
    [Fact]
    public async Task StalledBodyTimesOutAndPreservesExistingFacts()
    {
        var logger = new RecordingLogger();
        var service = Create(new StalledStream(), logger);
        await service.EnrichAsync(Guid.NewGuid(), Documents, DateTimeOffset.UtcNow, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsAssignableFrom<OperationCanceledException>(logger.Exception);
    }

    [Fact]
    public async Task CallerCancellationIsNotSwallowed()
    {
        var service = Create(new StalledStream(), new RecordingLogger());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EnrichAsync(Guid.NewGuid(), Documents, DateTimeOffset.UtcNow, cancellation.Token));
    }

    [Fact]
    public async Task OversizedBodyIsRejectedBeforeParsing()
    {
        var logger = new RecordingLogger();
        await Create(new MemoryStream(new byte[2048]), logger).EnrichAsync(Guid.NewGuid(), Documents,
            DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.IsType<InvalidDataException>(logger.Exception);
    }

    private static IpoDocumentEnrichmentService Create(Stream body, RecordingLogger logger) => new(
        new HttpClient(new ResponseHandler(body)), new IpoOfferDocumentSelector(), new PdfPigTextExtractor(),
        new IpoOfferDocumentParser(), new IpoRepository(null!),
        Options.Create(new DocumentEnrichmentOptions { DownloadTimeoutSeconds = 1, MaxDocumentBytes = 1024 }), logger);

    private static readonly IpoDocumentRecord[] Documents = [new("RHP", "RHP", "https://example.com/rhp.pdf",
        "test", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)];

    private sealed class ResponseHandler(Stream body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingLogger : ILogger<IpoDocumentEnrichmentService>
    {
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Exception = exception;
    }
}
