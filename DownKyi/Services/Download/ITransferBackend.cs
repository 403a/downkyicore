using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Domain.Downloads;
using Downloader;

namespace DownKyi.Services.Download;

internal sealed record DownloadTransferRequest(
    DownloadTaskId TaskId,
    string? BackendIdentity,
    IReadOnlyList<string> Urls,
    string Directory,
    string FileName,
    long ExpectedBytes,
    Action EnsureActive,
    Func<bool> IsPauseRequested,
    Action<DownloadProgress> PublishProgress,
    Func<DownloadProgress, CancellationToken, Task> PersistProgressAsync,
    Func<string?, CancellationToken, Task> SetBackendIdentityAsync,
    Action<DownloadService?> SetBuiltinDownloadService,
    CancellationToken CancellationToken);

internal interface ITransferBackend : IDisposable
{
    string Name { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task<DownloadTransferOutcome> TransferAsync(DownloadTransferRequest request);
}

internal enum DownloadTransferOutcome
{
    Failed,
    Succeeded,
    Paused
}
