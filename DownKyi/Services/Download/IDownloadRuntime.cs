using System;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Domain.Downloads;

namespace DownKyi.Services.Download;

internal interface IDownloadTaskQueue
{
    Task EnqueueAsync(DownloadTaskId taskId, CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(DownloadTaskId taskId);
}

internal interface IDownloadRuntime : IDownloadTaskQueue, IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
