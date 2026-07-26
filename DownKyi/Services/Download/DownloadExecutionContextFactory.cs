using System;
using System.Threading;
using DownKyi.Core.Settings;
using DownKyi.Domain.Downloads;

namespace DownKyi.Services.Download;

internal sealed class DownloadExecutionContextFactory
{
    private readonly DownloadTaskProjectionStore _projectionStore;
    private readonly ISettingsStore _settingsStore;

    public DownloadExecutionContextFactory(
        DownloadTaskProjectionStore projectionStore,
        ISettingsStore settingsStore)
    {
        _projectionStore = projectionStore
            ?? throw new ArgumentNullException(nameof(projectionStore));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public DownloadExecutionContext Create(DownloadTaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        return new DownloadExecutionContext(
            taskId,
            _projectionStore.GetRequiredDownloadingProjection(taskId),
            _settingsStore.Current,
            EnsureActive);
    }

    private void EnsureActive(
        DownloadTaskId taskId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = _projectionStore.GetRequiredSnapshot(taskId);
        if (task.Phase != DownloadPhase.Downloading)
        {
            throw new OperationCanceledException("Task is paused or deleted.");
        }
    }
}
