using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DownKyi.Services.Download;

/// <summary>
/// Downloads one resolved address as bounded HTTP ranges. The fixed-size range queue,
/// resumable per-range state and read-stall watchdog are adapted from BBDown's MIT-licensed
/// download pipeline. See THIRD-PARTY-NOTICES.md.
/// </summary>
internal sealed class BuiltinRangeDownloader : IDisposable
{
    internal const long DefaultSegmentSize = 20L * 1024 * 1024;
    internal static readonly TimeSpan DefaultReadStallTimeout = TimeSpan.FromSeconds(60);

    private const int BufferSize = 256 * 1024;
    private const int MaximumResumeMetadataBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions ResumeJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Uri _address;
    private readonly HttpMessageInvoker _http;
    private readonly AriaTaskHeaders _headers;
    private readonly ILogger _logger;
    private readonly int _parallelCount;
    private readonly Action<long, long> _progress;
    private readonly TimeSpan _readStallTimeout;
    private readonly long _segmentSize;
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private long _lastCheckpointTimestamp;
    private bool _disposed;

    public BuiltinRangeDownloader(
        HttpMessageHandler handler,
        Uri address,
        AriaTaskHeaders headers,
        int parallelCount,
        Action<long, long> progress,
        ILogger logger,
        long segmentSize = DefaultSegmentSize,
        TimeSpan? readStallTimeout = null,
        bool disposeHandler = true)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _headers = headers ?? throw new ArgumentNullException(nameof(headers));
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentSize, 1);
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parallelCount = parallelCount;
        _segmentSize = segmentSize;
        _readStallTimeout = readStallTimeout ?? DefaultReadStallTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _readStallTimeout,
            TimeSpan.Zero);

        _http = new HttpMessageInvoker(handler, disposeHandler);
    }

    public async Task<BuiltinRangeDownloadResult> DownloadAsync(
        string targetFile,
        long expectedBytes,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFile);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedBytes);
        cancellationToken.ThrowIfCancellationRequested();

        var recovered = TryRecoverCompletedTarget(targetFile, expectedBytes);
        if (recovered != null)
        {
            return recovered;
        }

        var probe = await ProbeAsync(expectedBytes, cancellationToken).ConfigureAwait(false);
        recovered = TryRecoverCompletedTarget(targetFile, probe.TotalBytes);
        if (recovered != null)
        {
            return recovered;
        }

        var partialFile = $"{targetFile}.download";
        var state = await LoadOrCreateStateAsync(
            partialFile,
            probe.TotalBytes,
            probe.SupportsRanges,
            probe.ResourceIdentity,
            cancellationToken).ConfigureAwait(false);
        var receivedBytes = state.Chunks.Sum(static chunk => chunk.Position);
        _progress(receivedBytes, probe.TotalBytes);

        var storage = new FileStream(
            partialFile,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        await using var storageLifetime = storage.ConfigureAwait(false);
        _lastCheckpointTimestamp = Stopwatch.GetTimestamp();
        Exception? primaryFailure = null;
        try
        {
            receivedBytes = await DownloadPendingChunksAsync(
                storage,
                state,
                probe.SupportsRanges,
                receivedBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            try
            {
                await SaveCheckpointAsync(storage, state, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception checkpointException) when (checkpointException is IOException
                or UnauthorizedAccessException)
            {
                _logger.LogWarningMessage(
                    $"Built-in range checkpoint failed while preserving the primary " +
                    $"transfer failure; type={checkpointException.GetType().Name}.");
            }

            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
            throw;
        }

        if (state.Chunks.Any(static chunk => !chunk.IsComplete))
        {
            throw new HttpIOException(
                HttpRequestError.ResponseEnded,
                "The range queue ended before every segment completed.");
        }

        storage.SetLength(probe.TotalBytes);
        await storage.FlushAsync(cancellationToken).ConfigureAwait(false);
        await storage.DisposeAsync().ConfigureAwait(false);
        File.Move(partialFile, targetFile, overwrite: false);
        _progress(probe.TotalBytes, probe.TotalBytes);
        return new BuiltinRangeDownloadResult(receivedBytes, probe.TotalBytes);
    }

    internal static IReadOnlyList<BuiltinRangeChunk> CreateChunks(
        long totalBytes,
        long segmentSize = DefaultSegmentSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentSize, 1);
        var chunks = new List<BuiltinRangeChunk>();
        for (var start = 0L; start < totalBytes; start = checked(start + segmentSize))
        {
            var end = Math.Min(totalBytes - 1, checked(start + segmentSize - 1));
            chunks.Add(new BuiltinRangeChunk(start, end, position: 0));
        }

        return chunks;
    }

    private async Task<BuiltinRangeProbe> ProbeAsync(
        long expectedBytes,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(rangeStart: 0, rangeEnd: 0);
        using var response = await SendWithStallTimeoutAsync(request, cancellationToken)
            .ConfigureAwait(false);
        ThrowForHttpFailure(response);

        long totalBytes;
        bool supportsRanges;
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange is not { From: 0, To: 0, Length: > 0 }
                || !string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuiltinResumeRejectedException(
                    "The range probe returned an invalid Content-Range header.");
            }

            totalBytes = contentRange.Length.Value;
            supportsRanges = true;
        }
        else if (response.StatusCode == HttpStatusCode.OK)
        {
            totalBytes = response.Content.Headers.ContentLength ?? expectedBytes;
            supportsRanges = false;
        }
        else
        {
            throw new BuiltinHttpStatusException(response.StatusCode, retryAfter: null);
        }

        if (totalBytes <= 0)
        {
            throw new HttpIOException(
                HttpRequestError.InvalidResponse,
                "The media response did not declare its total length.");
        }

        if (expectedBytes > 0 && expectedBytes != totalBytes)
        {
            throw new BuiltinResumeRejectedException(
                "The media length changed before the transfer started.");
        }

        return new BuiltinRangeProbe(
            totalBytes,
            supportsRanges,
            BuiltinRangeResourceIdentity.Create(_address, response));
    }

    private async Task<BuiltinRangeResumeState> LoadOrCreateStateAsync(
        string partialFile,
        long totalBytes,
        bool supportsRanges,
        BuiltinRangeResourceIdentity resourceIdentity,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(partialFile))
        {
            return await CreateFreshStateAsync(
                partialFile,
                totalBytes,
                supportsRanges,
                resourceIdentity,
                cancellationToken).ConfigureAwait(false);
        }

        if ((File.GetAttributes(partialFile) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The built-in partial file cannot be a redirected file.");
        }

        var stream = new FileStream(
            partialFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var streamLifetime = stream.ConfigureAwait(false);
        var metadataLength = stream.Length - totalBytes;
        if (metadataLength <= 0 || metadataLength > MaximumResumeMetadataBytes)
        {
            throw new BuiltinResumeRejectedException(
                "The built-in partial file has no valid resume metadata.");
        }

        var metadata = new byte[checked((int)metadataLength)];
        stream.Position = totalBytes;
        await stream.ReadExactlyAsync(metadata, cancellationToken).ConfigureAwait(false);
        BuiltinRangeResumeDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<BuiltinRangeResumeDocument>(
                metadata,
                ResumeJsonOptions);
        }
        catch (JsonException exception)
        {
            throw new BuiltinResumeRejectedException(
                "The built-in resume metadata is malformed.",
                exception);
        }

        if (document == null
            || document.TotalFileSize != totalBytes
            || document.ResourceIdentity == null
            || !document.ResourceIdentity.CanResume
            || !document.ResourceIdentity.Matches(resourceIdentity)
            || !TryValidateChunks(document.Chunks, totalBytes, out var chunks))
        {
            throw new BuiltinResumeRejectedException(
                "The built-in resume metadata does not match the media file.");
        }

        if (!supportsRanges && chunks.Any(static chunk => chunk.Position > 0))
        {
            throw new BuiltinResumeRejectedException(
                "The server no longer supports resuming the partial media file.");
        }

        return new BuiltinRangeResumeState(
            totalBytes,
            chunks,
            resourceIdentity,
            HasResumedBytes: chunks.Any(static chunk => chunk.Position > 0));
    }

    private async Task<BuiltinRangeResumeState> CreateFreshStateAsync(
        string partialFile,
        long totalBytes,
        bool supportsRanges,
        BuiltinRangeResourceIdentity resourceIdentity,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(partialFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var chunks = supportsRanges
            ? CreateChunks(totalBytes, _segmentSize).ToArray()
            : [new BuiltinRangeChunk(0, totalBytes - 1, position: 0)];
        var stream = new FileStream(
            partialFile,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        await using (var streamLifetime = stream.ConfigureAwait(false))
        {
            stream.SetLength(totalBytes);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return new BuiltinRangeResumeState(
            totalBytes,
            chunks,
            resourceIdentity,
            HasResumedBytes: false);
    }

    private async Task<long> DownloadPendingChunksAsync(
        FileStream storage,
        BuiltinRangeResumeState state,
        bool supportsRanges,
        long initialReceivedBytes,
        CancellationToken cancellationToken)
    {
        var queue = new ConcurrentQueue<BuiltinRangeChunk>(
            state.Chunks.Where(static chunk => !chunk.IsComplete));
        if (queue.IsEmpty)
        {
            return initialReceivedBytes;
        }

        using var workersCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        ExceptionDispatchInfo? firstFailure = null;
        var receivedBytes = initialReceivedBytes;
        var workerCount = Math.Min(_parallelCount, queue.Count);
        var workers = Enumerable.Range(0, workerCount)
            .Select(async _ =>
            {
                while (queue.TryDequeue(out var chunk))
                {
                    try
                    {
                        await DownloadChunkAsync(
                            storage,
                            state,
                            chunk,
                            supportsRanges,
                            bytesReceived =>
                            {
                                var total = Interlocked.Add(ref receivedBytes, bytesReceived);
                                _progress(total, state.TotalBytes);
                            },
                            workersCancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (
                        workersCancellation.IsCancellationRequested && firstFailure != null)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(
                            ref firstFailure,
                            ExceptionDispatchInfo.Capture(exception),
                            null);
                        await workersCancellation.CancelAsync().ConfigureAwait(false);
                        throw;
                    }
                }
            })
            .ToArray();

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            firstFailure?.Throw();
            throw;
        }

        return receivedBytes;
    }

    private async Task DownloadChunkAsync(
        FileStream storage,
        BuiltinRangeResumeState state,
        BuiltinRangeChunk chunk,
        bool supportsRanges,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        var requestStart = checked(chunk.Start + chunk.Position);
        using var request = supportsRanges
            ? CreateRequest(
                requestStart,
                chunk.End,
                state.ResourceIdentity.IfRangeValue)
            : CreateRequest(rangeStart: null, rangeEnd: null);
        using var response = await SendWithStallTimeoutAsync(request, cancellationToken)
            .ConfigureAwait(false);
        ValidateChunkResponse(
            response,
            requestStart,
            chunk.End,
            state.TotalBytes,
            supportsRanges,
            state.ResourceIdentity,
            state.HasResumedBytes);

        var remaining = checked(chunk.End - requestStart + 1);
        var source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var sourceLifetime = source.ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (remaining > 0)
            {
                var read = await ReadWithStallTimeoutAsync(
                    source,
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new HttpIOException(
                        HttpRequestError.ResponseEnded,
                        "The media response ended before the requested range completed.");
                }

                var fileOffset = checked(chunk.Start + chunk.Position);
                await RandomAccess.WriteAsync(
                    storage.SafeFileHandle,
                    buffer.AsMemory(0, read),
                    fileOffset,
                    cancellationToken).ConfigureAwait(false);
                chunk.Advance(read);
                remaining -= read;
                reportBytes(read);
                await SaveCheckpointIfDueAsync(
                    storage,
                    state,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private BuiltinRangeDownloadResult? TryRecoverCompletedTarget(
        string targetFile,
        long expectedBytes)
    {
        if (!TryGetCompletedTarget(targetFile, expectedBytes, out var result))
        {
            return null;
        }

        _progress(result.ReceivedBytes, result.TotalBytes);
        return result;
    }

    internal static bool TryGetCompletedTarget(
        string targetFile,
        long expectedBytes,
        out BuiltinRangeDownloadResult result)
    {
        result = null!;
        if (expectedBytes <= 0 || !File.Exists(targetFile))
        {
            return false;
        }

        if ((File.GetAttributes(targetFile) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The completed built-in download cannot be a redirected file.");
        }

        var actualBytes = new FileInfo(targetFile).Length;
        if (actualBytes != expectedBytes)
        {
            throw new BuiltinResumeRejectedException(
                "The completed built-in download does not match the expected length.");
        }

        var integrity = DownloadFileIntegrity.Check(targetFile, expectedBytes);
        if (!integrity.IsUsable)
        {
            throw new BuiltinResumeRejectedException(
                integrity.Reason ?? "The completed built-in download is not usable.");
        }

        result = new BuiltinRangeDownloadResult(actualBytes, expectedBytes);
        return true;
    }

    private async Task<HttpResponseMessage> SendWithStallTimeoutAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var stallCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        stallCancellation.CancelAfter(_readStallTimeout);
        try
        {
            return await _http.SendAsync(request, stallCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested
            && stallCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The media server stopped before sending response headers.",
                exception);
        }
    }

    private async ValueTask<int> ReadWithStallTimeoutAsync(
        Stream source,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        using var stallCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        stallCancellation.CancelAfter(_readStallTimeout);
        try
        {
            return await source.ReadAsync(buffer, stallCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The media response stopped producing data.",
                exception);
        }
    }

    private async Task SaveCheckpointIfDueAsync(
        FileStream storage,
        BuiltinRangeResumeState state,
        CancellationToken cancellationToken)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref _lastCheckpointTimestamp);
        if (Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromSeconds(1)
            || Interlocked.CompareExchange(
                ref _lastCheckpointTimestamp,
                now,
                previous) != previous)
        {
            return;
        }

        await SaveCheckpointAsync(storage, state, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveCheckpointAsync(
        FileStream storage,
        BuiltinRangeResumeState state,
        CancellationToken cancellationToken)
    {
        await _checkpointGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = new BuiltinRangeResumeDocument(
                state.TotalBytes,
                state.Chunks.Select(static chunk => new BuiltinRangeResumeChunk(
                    chunk.Start,
                    chunk.End,
                    chunk.Position)).ToArray(),
                state.ResourceIdentity);
            var metadata = JsonSerializer.SerializeToUtf8Bytes(document, ResumeJsonOptions);
            if (metadata.Length > MaximumResumeMetadataBytes)
            {
                throw new IOException("The built-in resume metadata exceeded its safety limit.");
            }

            await storage.FlushAsync(cancellationToken).ConfigureAwait(false);
            storage.SetLength(state.TotalBytes);
            await RandomAccess.WriteAsync(
                storage.SafeFileHandle,
                metadata,
                state.TotalBytes,
                cancellationToken).ConfigureAwait(false);
            await storage.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _checkpointGate.Release();
        }
    }

    private HttpRequestMessage CreateRequest(
        long? rangeStart,
        long? rangeEnd,
        string? ifRange = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _address);
        if (rangeStart.HasValue)
        {
            request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);
            if (!string.IsNullOrWhiteSpace(ifRange))
            {
                request.Headers.TryAddWithoutValidation("If-Range", ifRange);
            }
        }

        request.Headers.AcceptEncoding.ParseAdd("identity");
        request.Headers.TryAddWithoutValidation("User-Agent", _headers.UserAgent);
        foreach (var header in _headers.Headers)
        {
            var separator = header.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                request.Dispose();
                throw new InvalidOperationException("A built-in download header is malformed.");
            }

            request.Headers.TryAddWithoutValidation(
                header[..separator],
                header[(separator + 1)..].TrimStart());
        }

        return request;
    }

    private static void ValidateChunkResponse(
        HttpResponseMessage response,
        long requestStart,
        long requestEnd,
        long totalBytes,
        bool supportsRanges,
        BuiltinRangeResourceIdentity resourceIdentity,
        bool requireResourceValidator)
    {
        ThrowForHttpFailure(response);
        var expectedLength = checked(requestEnd - requestStart + 1);
        if (supportsRanges)
        {
            var contentRange = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent
                || contentRange?.From != requestStart
                || contentRange.To != requestEnd
                || contentRange.Length != totalBytes
                || !string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuiltinResumeRejectedException(
                    "The media server did not honor the requested byte range.");
            }
        }
        else if (response.StatusCode != HttpStatusCode.OK || requestStart != 0)
        {
            throw new BuiltinResumeRejectedException(
                "The media server cannot resume the partial response.");
        }

        if (response.Content.Headers.ContentLength is { } contentLength
            && contentLength != expectedLength)
        {
            throw new BuiltinResumeRejectedException(
                "The media response length did not match the requested byte range.");
        }

        if (supportsRanges
            && !resourceIdentity.IsCompatibleWith(response, requireResourceValidator))
        {
            throw new BuiltinResumeRejectedException(
                "The media response resource identity changed during the transfer.");
        }
    }

    private static void ThrowForHttpFailure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter == null && response.Headers.RetryAfter?.Date is { } retryDate)
        {
            retryAfter = retryDate - DateTimeOffset.UtcNow;
            if (retryAfter < TimeSpan.Zero)
            {
                retryAfter = TimeSpan.Zero;
            }
        }

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            throw new BuiltinResumeRejectedException(
                "The media server rejected the requested resume range.");
        }

        throw new BuiltinHttpStatusException(response.StatusCode, retryAfter);
    }

    private static bool TryValidateChunks(
        BuiltinRangeResumeChunk[]? serializedChunks,
        long totalBytes,
        out BuiltinRangeChunk[] chunks)
    {
        chunks = [];
        if (serializedChunks is not { Length: > 0 })
        {
            return false;
        }

        var validated = new BuiltinRangeChunk[serializedChunks.Length];
        var expectedStart = 0L;
        for (var index = 0; index < serializedChunks.Length; index++)
        {
            var serialized = serializedChunks[index];
            if (serialized.Start != expectedStart
                || serialized.End < serialized.Start
                || serialized.End >= totalBytes)
            {
                return false;
            }

            var length = checked(serialized.End - serialized.Start + 1);
            if (serialized.Position < 0 || serialized.Position > length)
            {
                return false;
            }

            validated[index] = new BuiltinRangeChunk(
                serialized.Start,
                serialized.End,
                serialized.Position);
            expectedStart = checked(serialized.End + 1);
        }

        if (expectedStart != totalBytes)
        {
            return false;
        }

        chunks = validated;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
        _checkpointGate.Dispose();
    }
}

internal sealed class BuiltinRangeChunk(long start, long end, long position)
{
    private long _position = position;

    public long Start { get; } = start;

    public long End { get; } = end;

    public long Position => Interlocked.Read(ref _position);

    public bool IsComplete => Position >= checked(End - Start + 1);

    public void Advance(int count) => Interlocked.Add(ref _position, count);
}

internal sealed record BuiltinRangeDownloadResult(long ReceivedBytes, long TotalBytes);

internal sealed record BuiltinRangeProbe(
    long TotalBytes,
    bool SupportsRanges,
    BuiltinRangeResourceIdentity ResourceIdentity);

internal sealed record BuiltinRangeResumeState(
    long TotalBytes,
    BuiltinRangeChunk[] Chunks,
    BuiltinRangeResourceIdentity ResourceIdentity,
    bool HasResumedBytes);

internal sealed record BuiltinRangeResumeDocument(
    long TotalFileSize,
    BuiltinRangeResumeChunk[] Chunks,
    BuiltinRangeResourceIdentity? ResourceIdentity = null);

internal sealed record BuiltinRangeResumeChunk(long Start, long End, long Position);

internal sealed record BuiltinRangeResourceIdentity(
    string AddressHash,
    string? StrongETag,
    DateTimeOffset? LastModified)
{
    private static readonly TimeSpan MinimumStrongLastModifiedAge =
        TimeSpan.FromSeconds(60);

    public bool CanResume => StrongETag != null || LastModified != null;

    public string? IfRangeValue => StrongETag ?? LastModified?.ToString("R");

    public static BuiltinRangeResourceIdentity Create(
        Uri address,
        HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(response);
        var entityTag = response.Headers.ETag is { IsWeak: false } strongEntityTag
            ? strongEntityTag.ToString()
            : null;
        var lastModified = response.Content.Headers.LastModified is { } modified
                           && response.Headers.Date is { } responseDate
                           && responseDate - modified >= MinimumStrongLastModifiedAge
            ? (DateTimeOffset?)modified
            : null;
        return new BuiltinRangeResourceIdentity(
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(address.AbsoluteUri))),
            entityTag,
            lastModified);
    }

    public bool Matches(BuiltinRangeResourceIdentity current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!string.Equals(AddressHash, current.AddressHash, StringComparison.Ordinal))
        {
            return false;
        }

        if (StrongETag != null && current.StrongETag != null)
        {
            return string.Equals(StrongETag, current.StrongETag, StringComparison.Ordinal);
        }

        return LastModified != null
               && current.LastModified != null
               && LastModified == current.LastModified;
    }

    public bool IsCompatibleWith(
        HttpResponseMessage response,
        bool requireValidator)
    {
        ArgumentNullException.ThrowIfNull(response);
        var validatorMatched = false;
        if (StrongETag != null && response.Headers.ETag is { } entityTag)
        {
            if (entityTag.IsWeak
                || !string.Equals(StrongETag, entityTag.ToString(), StringComparison.Ordinal))
            {
                return false;
            }

            validatorMatched = true;
        }

        if (LastModified != null
            && response.Content.Headers.LastModified is { } currentLastModified)
        {
            if (LastModified != currentLastModified)
            {
                return false;
            }

            validatorMatched = true;
        }

        return !requireValidator || validatorMatched;
    }
}

internal sealed class BuiltinResumeRejectedException : IOException
{
    public BuiltinResumeRejectedException()
    {
    }

    public BuiltinResumeRejectedException(string message)
        : base(message)
    {
    }

    public BuiltinResumeRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

}

internal sealed class BuiltinHttpStatusException : HttpRequestException
{
    public BuiltinHttpStatusException()
    {
    }

    public BuiltinHttpStatusException(string message)
        : base(message)
    {
    }

    public BuiltinHttpStatusException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BuiltinHttpStatusException(HttpStatusCode statusCode, TimeSpan? retryAfter)
        : base("The media server rejected the built-in download request.", null, statusCode)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}
