using System;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Core.Utils;
using DownKyi.Domain.Downloads;
using DownKyi.Domain.Results;
using DownKyi.Models;

namespace DownKyi.Services.Download;

internal sealed class FinalizeStage : IDownloadPipelineStage
{
    private readonly DownloadTaskProjectionStore _projectionStore;
    private readonly DownloadTaskStateWriter _stateWriter;
    private readonly DownloadCompletionProjector _completionProjector;
    private readonly TimeProvider _timeProvider;

    public FinalizeStage(
        DownloadTaskProjectionStore projectionStore,
        DownloadTaskStateWriter stateWriter,
        DownloadCompletionProjector completionProjector,
        TimeProvider timeProvider)
    {
        _projectionStore = projectionStore
            ?? throw new ArgumentNullException(nameof(projectionStore));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _completionProjector = completionProjector
            ?? throw new ArgumentNullException(nameof(completionProjector));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public string Name => nameof(FinalizeStage);

    public async Task<OperationResult<DownloadStageResult>> ExecuteAsync(
        DownloadExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.EnsureActive(cancellationToken);
        var downloaded = CreateDownloadedSummary(
            _projectionStore
                .GetRequiredSnapshot(context.TaskId)
                .Transfer
                .MaximumBytesPerSecond,
            _timeProvider);

        var completedTask = await _stateWriter.CompleteAsync(
            context.TaskId,
            new DownloadCompletion(
                downloaded.FinishedTimestamp,
                downloaded.FinishedTime,
                downloaded.MaxSpeedDisplay),
            cancellationToken).ConfigureAwait(true);
        await _completionProjector.ProjectAsync(context, completedTask).ConfigureAwait(true);

        return DownloadStageResult.Success(Name);
    }

    internal static Downloaded CreateDownloadedSummary(
        long maximumBytesPerSecond,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var downloaded = new Downloaded
        {
            MaxSpeedDisplay = Format.FormatSpeedWithBandwidth(maximumBytesPerSecond)
        };
        downloaded.SetFinishedTimestamp(timeProvider.GetUtcNow().ToUnixTimeSeconds());
        return downloaded;
    }
}
