using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DownKyi.Services.Download;
using Microsoft.Extensions.Logging.Abstractions;

namespace DownKyi.Tests;

public sealed class BuiltinRangeDownloaderTests
{
    [Fact]
    public async Task ExistingCompletedTargetIsRecoveredWithoutAnotherRequest()
    {
        var payload = CreatePayload(12);
        using var handler = new UnexpectedRequestHandler();
        var directory = CreateTemporaryDirectory("range-completed-recovery");
        var target = Path.Combine(directory, "media.bin");
        try
        {
            await File.WriteAllBytesAsync(
                target,
                payload,
                TestContext.Current.CancellationToken);
            using var downloader = CreateDownloader(
                handler,
                parallelCount: 2,
                segmentSize: 4);

            var result = await downloader.DownloadAsync(
                target,
                payload.Length,
                TestContext.Current.CancellationToken);

            Assert.Equal(payload.Length, result.TotalBytes);
            Assert.Equal(payload.Length, result.ReceivedBytes);
            Assert.Equal(0, handler.Requests);
            Assert.Equal(payload, await File.ReadAllBytesAsync(
                target,
                TestContext.Current.CancellationToken));
            Assert.False(File.Exists($"{target}.download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WorkersConsumeFixedRangesConcurrentlyAndProduceExactFile()
    {
        var payload = CreatePayload(12);
        using var handler = new RangePayloadHandler(payload, requiredConcurrentReads: 2);
        var directory = CreateTemporaryDirectory("range-workers");
        var target = Path.Combine(directory, "media.bin");
        try
        {
            using var downloader = CreateDownloader(
                handler,
                parallelCount: 2,
                segmentSize: 4);

            var result = await downloader.DownloadAsync(
                target,
                payload.Length,
                TestContext.Current.CancellationToken);

            Assert.Equal(payload.Length, result.TotalBytes);
            Assert.Equal(payload.Length, result.ReceivedBytes);
            Assert.Equal(2, handler.MaximumConcurrentReads);
            Assert.Equal(
                [(0L, 3L), (4L, 7L), (8L, 11L)],
                handler.ChunkRanges.OrderBy(static range => range.Start).ToArray());
            Assert.Equal(payload, await File.ReadAllBytesAsync(
                target,
                TestContext.Current.CancellationToken));
            Assert.False(File.Exists($"{target}.download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NextCoordinatorAttemptRequestsOnlyMissingRangeBytes()
    {
        var payload = CreatePayload(12);
        var directory = CreateTemporaryDirectory("range-resume");
        var target = Path.Combine(directory, "media.bin");
        try
        {
            using (var firstHandler = new RangePayloadHandler(
                payload,
                failOnceAtStart: 4,
                failureBytes: 2))
            using (var firstAttempt = CreateDownloader(
                firstHandler,
                parallelCount: 1,
                segmentSize: 4))
            {
                await Assert.ThrowsAsync<HttpIOException>(() => firstAttempt.DownloadAsync(
                    target,
                    payload.Length,
                    TestContext.Current.CancellationToken));
            }

            Assert.True(File.Exists($"{target}.download"));
            using var secondHandler = new RangePayloadHandler(payload);
            using var secondAttempt = CreateDownloader(
                secondHandler,
                parallelCount: 1,
                segmentSize: 4);

            await secondAttempt.DownloadAsync(
                target,
                payload.Length,
                TestContext.Current.CancellationToken);

            Assert.DoesNotContain(secondHandler.ChunkRanges, range => range.Start is 0 or 4);
            Assert.Contains(secondHandler.ChunkRanges, range => range == (6L, 7L));
            Assert.Contains(secondHandler.ChunkRanges, range => range == (8L, 11L));
            Assert.Equal(payload, await File.ReadAllBytesAsync(
                target,
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BodyWithoutDataTriggersBoundedTransientFailure()
    {
        var payload = CreatePayload(4);
        using var handler = new RangePayloadHandler(payload, stallAtStart: 0);
        var directory = CreateTemporaryDirectory("range-stall");
        var target = Path.Combine(directory, "media.bin");
        try
        {
            using var downloader = CreateDownloader(
                handler,
                parallelCount: 1,
                segmentSize: 4,
                readStallTimeout: TimeSpan.FromMilliseconds(50));

            var error = await Assert.ThrowsAsync<TimeoutException>(() => downloader.DownloadAsync(
                target,
                payload.Length,
                TestContext.Current.CancellationToken));
            var classified = BuiltinTransferBackend.ClassifyFailure(
                error,
                reportedCanceled: false);

            Assert.Equal(DownloadTransferFailureKind.TransientNetwork, classified.FailureKind);
            Assert.True(File.Exists($"{target}.download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ResponseHeadersWithoutDataTriggerBoundedTransientFailure(
        int stalledRequest)
    {
        var payload = CreatePayload(4);
        using var handler = new RangePayloadHandler(
            payload,
            stallBeforeHeadersAtRequest: stalledRequest);
        var directory = CreateTemporaryDirectory("range-header-stall");
        var target = Path.Combine(directory, "media.bin");
        try
        {
            using var downloader = CreateDownloader(
                handler,
                parallelCount: 1,
                segmentSize: 4,
                readStallTimeout: TimeSpan.FromMilliseconds(50));

            var error = await Assert.ThrowsAsync<TimeoutException>(() => downloader.DownloadAsync(
                target,
                payload.Length,
                TestContext.Current.CancellationToken));
            var classified = BuiltinTransferBackend.ClassifyFailure(
                error,
                reportedCanceled: false);

            Assert.Equal(DownloadTransferFailureKind.TransientNetwork, classified.FailureKind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingResponseLengthIsAProtocolFailureInsteadOfADiskFailure()
    {
        using var handler = new UnknownLengthHandler();
        var directory = CreateTemporaryDirectory("range-missing-length");
        var target = Path.Combine(directory, "media.bin");
        try
        {
            using var downloader = CreateDownloader(
                handler,
                parallelCount: 1,
                segmentSize: 4);

            var error = await Assert.ThrowsAsync<HttpIOException>(() => downloader.DownloadAsync(
                target,
                expectedBytes: 0,
                cancellationToken: TestContext.Current.CancellationToken));
            var classified = BuiltinTransferBackend.ClassifyFailure(
                error,
                reportedCanceled: false);

            Assert.Equal(DownloadTransferFailureKind.TransientNetwork, classified.FailureKind);
            Assert.Equal(HttpRequestError.InvalidResponse, error.HttpRequestError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyDownloaderMetadataResumesWithoutRedownloadingCompletedRange()
    {
        var payload = CreatePayload(12);
        var directory = CreateTemporaryDirectory("legacy-range-resume");
        var target = Path.Combine(directory, "media.bin");
        var partial = $"{target}.download";
        try
        {
            var metadata = Encoding.UTF8.GetBytes(
                "{\"TotalFileSize\":12,\"Chunks\":[" +
                "{\"Start\":0,\"End\":3,\"Position\":4,\"MaxTryAgainOnFailure\":0,\"Timeout\":5000}," +
                "{\"Start\":4,\"End\":7,\"Position\":0,\"MaxTryAgainOnFailure\":0,\"Timeout\":5000}," +
                "{\"Start\":8,\"End\":11,\"Position\":0,\"MaxTryAgainOnFailure\":0,\"Timeout\":5000}]}");
            var stream = new FileStream(
                partial,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous);
            await using (var streamLifetime = stream.ConfigureAwait(false))
            {
                stream.SetLength(payload.Length);
                await stream.WriteAsync(
                    payload.AsMemory(0, 4),
                    TestContext.Current.CancellationToken);
                stream.Position = payload.Length;
                await stream.WriteAsync(metadata, TestContext.Current.CancellationToken);
            }

            using var handler = new RangePayloadHandler(payload);
            using var downloader = CreateDownloader(
                handler,
                parallelCount: 2,
                segmentSize: 4);

            await downloader.DownloadAsync(
                target,
                payload.Length,
                TestContext.Current.CancellationToken);

            Assert.DoesNotContain(handler.ChunkRanges, range => range.Start == 0);
            Assert.Equal(payload, await File.ReadAllBytesAsync(
                target,
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static BuiltinRangeDownloader CreateDownloader(
        HttpMessageHandler handler,
        int parallelCount,
        long segmentSize,
        TimeSpan? readStallTimeout = null)
    {
        return new BuiltinRangeDownloader(
            handler,
            new Uri("https://media.invalid/file"),
            new AriaTaskHeaders(
                ["Origin: https://www.bilibili.com", "Referer: https://www.bilibili.com"],
                "DownKyi-Test",
                CarriesCredentials: false),
            parallelCount,
            static (_, _) => { },
            NullLogger.Instance,
            segmentSize,
            readStallTimeout,
            disposeHandler: false);
    }

    private static byte[] CreatePayload(int length) =>
        Enumerable.Range(0, length).Select(static value => checked((byte)value)).ToArray();

    private static string CreateTemporaryDirectory(string purpose)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"downkyi-{purpose}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class RangePayloadHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly int? _failOnceAtStart;
        private readonly int _failureBytes;
        private readonly ConcurrentQueue<(long Start, long End)> _chunkRanges = new();
        private readonly ConcurrentReadGate? _readGate;
        private readonly int? _stallAtStart;
        private readonly int? _stallBeforeHeadersAtRequest;
        private int _failureReturned;
        private int _requests;

        public RangePayloadHandler(
            byte[] payload,
            int requiredConcurrentReads = 0,
            int? failOnceAtStart = null,
            int failureBytes = 0,
            int? stallAtStart = null,
            int? stallBeforeHeadersAtRequest = null)
        {
            _payload = payload ?? throw new ArgumentNullException(nameof(payload));
            _failOnceAtStart = failOnceAtStart;
            _failureBytes = failureBytes;
            _stallAtStart = stallAtStart;
            _stallBeforeHeadersAtRequest = stallBeforeHeadersAtRequest;
            if (requiredConcurrentReads > 0)
            {
                _readGate = new ConcurrentReadGate(requiredConcurrentReads);
            }
        }

        public IReadOnlyCollection<(long Start, long End)> ChunkRanges =>
            _chunkRanges.ToArray();

        public int MaximumConcurrentReads => _readGate?.MaximumConcurrentReads ?? 0;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _requests) == _stallBeforeHeadersAtRequest)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            }

            var range = Assert.Single(request.Headers.Range?.Ranges ?? []);
            var start = range.From ?? 0;
            var end = range.To ?? (_payload.LongLength - 1);
            var isProbe = start == 0 && end == 0;
            if (!isProbe)
            {
                _chunkRanges.Enqueue((start, end));
            }

            Stream contentStream;
            var declaredLength = checked((int)(end - start + 1));
            if (!isProbe && _stallAtStart == start)
            {
                contentStream = new StallingStream();
            }
            else
            {
                var actualLength = declaredLength;
                if (!isProbe
                    && _failOnceAtStart == start
                    && Interlocked.Exchange(ref _failureReturned, 1) == 0)
                {
                    actualLength = Math.Min(_failureBytes, declaredLength);
                }

                var body = _payload.AsMemory(checked((int)start), actualLength).ToArray();
                contentStream = new MemoryStream(body, writable: false);
                if (!isProbe && _readGate != null)
                {
                    contentStream = new GatedReadStream(contentStream, _readGate);
                }
            }

            var content = new StreamContent(contentStream);
            content.Headers.ContentLength = declaredLength;
            content.Headers.ContentRange = new ContentRangeHeaderValue(
                start,
                end,
                _payload.LongLength);
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = content,
                RequestMessage = request
            };
        }
    }

    private sealed class UnexpectedRequestHandler : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return Task.FromException<HttpResponseMessage>(
                new InvalidOperationException("The completed target must not be downloaded again."));
        }
    }

    private sealed class UnknownLengthHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(),
                RequestMessage = request
            });
        }
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ConcurrentReadGate(int requiredConcurrentReads)
    {
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeReads;
        private int _maximumConcurrentReads;

        public int MaximumConcurrentReads => Volatile.Read(ref _maximumConcurrentReads);

        public async Task EnterAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeReads);
            UpdateMaximum(active);
            if (active >= requiredConcurrentReads)
            {
                _ready.TrySetResult();
            }

            await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Exit() => Interlocked.Decrement(ref _activeReads);

        private void UpdateMaximum(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumConcurrentReads);
                if (active <= current
                    || Interlocked.CompareExchange(
                        ref _maximumConcurrentReads,
                        active,
                        current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class GatedReadStream(Stream inner, ConcurrentReadGate gate) : Stream
    {
        private int _entered;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _entered, 1) == 0)
            {
                await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
            }

            return await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                if (Interlocked.Exchange(ref _entered, 0) != 0)
                {
                    gate.Exit();
                }
            }

            base.Dispose(disposing);
        }
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
