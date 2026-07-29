using System;
using System.Threading.Tasks;
using DownKyi.Application.Desktop;
using DownKyi.Domain.Downloads;
using DownKyi.Platform;

namespace DownKyi.Services.Download;

internal sealed class DownloadCompletionProjector
{
    private readonly DownloadListState _downloadLists;
    private readonly IUiDispatcher _uiDispatcher;

    public DownloadCompletionProjector(
        DownloadListState downloadLists,
        IUiDispatcher uiDispatcher)
    {
        _downloadLists = downloadLists ?? throw new ArgumentNullException(nameof(downloadLists));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    }

    public Task ProjectAsync(
        DownloadExecutionContext context,
        DownloadTask completedTask)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completedTask);
        var downloadedItem =
            DownloadTaskProjectionStore.CreateDownloadedProjection(completedTask);
        return _uiDispatcher.InvokeAsync(() =>
        {
            _downloadLists.AddDownloaded(downloadedItem);
            _downloadLists.RemoveDownloading(context.Downloading);
            _downloadLists.SortDownloaded(context.Settings.Basic.DownloadFinishedSort);
        });
    }
}
