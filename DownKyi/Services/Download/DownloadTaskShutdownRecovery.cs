using System;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Domain.Downloads;

namespace DownKyi.Services.Download;

internal sealed class DownloadTaskShutdownRecovery
{
    private readonly DownloadListState _downloadLists;
    private readonly DownloadTaskProjectionStore _projections;
    private readonly DownloadTaskStateWriter _stateWriter;

    public DownloadTaskShutdownRecovery(
        DownloadListState downloadLists,
        DownloadTaskProjectionStore projections,
        DownloadTaskStateWriter stateWriter)
    {
        _downloadLists = downloadLists ?? throw new ArgumentNullException(nameof(downloadLists));
        _projections = projections ?? throw new ArgumentNullException(nameof(projections));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
    }

    public async Task PersistAsync()
    {
        foreach (var item in _downloadLists.Downloading)
        {
            var taskId = new DownloadTaskId(item.DownloadBase.Id);
            var phase = _projections.GetRequiredSnapshot(taskId).Phase;
            if (phase is DownloadPhase.Downloading or DownloadPhase.Pausing)
            {
                await _stateWriter.RecoverInterruptedAsync(taskId, CancellationToken.None)
                    .ConfigureAwait(true);
            }
        }
    }
}
