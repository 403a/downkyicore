using System;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Domain.Downloads;

namespace DownKyi.Services.Download;

internal interface IDownloadTaskExecutor : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task ExecuteAsync(DownloadTaskId taskId, CancellationToken cancellationToken);

    Task MarkFailedAsync(
        DownloadTaskId taskId,
        CancellationToken cancellationToken = default);

    Task PersistShutdownStateAsync();
}
