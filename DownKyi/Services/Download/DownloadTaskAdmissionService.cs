using System;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Domain.Downloads;
using DownKyi.ViewModels.DownloadManager;

namespace DownKyi.Services.Download;

internal sealed class DownloadTaskAdmissionService
{
    private readonly DownloadListState _downloadLists;
    private readonly DownloadTaskProjectionStore _projections;
    private readonly IDownloadTaskQueue _taskQueue;

    public DownloadTaskAdmissionService(
        DownloadListState downloadLists,
        DownloadTaskProjectionStore projections,
        IDownloadTaskQueue taskQueue)
    {
        _downloadLists = downloadLists ?? throw new ArgumentNullException(nameof(downloadLists));
        _projections = projections ?? throw new ArgumentNullException(nameof(projections));
        _taskQueue = taskQueue ?? throw new ArgumentNullException(nameof(taskQueue));
    }

    public async Task AdmitAsync(
        DownloadingItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _projections.AddDownloadingAsync(item, cancellationToken).ConfigureAwait(true);

        // Once persisted, admission must finish even if the originating UI operation is canceled.
        _downloadLists.Downloading.Add(item);
        await _taskQueue.EnqueueAsync(
            new DownloadTaskId(item.DownloadBase.Id),
            CancellationToken.None).ConfigureAwait(true);
    }
}
