namespace DownKyi.Application.Downloads;

public static class DownloadAddCoordinator
{
    public static async Task<DownloadAddFlowResult<TResult>> AddToDownloadIfSelectionAcceptedAsync<TResult>(
        Func<Task<DownloadAdmissionOutcome>> ensureAdmissionAsync,
        Func<Task<DownloadSelectionResult>> selectDownloadAsync,
        Func<DownloadAddSelection, Task<TResult>> addToDownloadAsync,
        CancellationToken cancellationToken = default)
        where TResult : struct
    {
        ArgumentNullException.ThrowIfNull(ensureAdmissionAsync);
        ArgumentNullException.ThrowIfNull(selectDownloadAsync);
        ArgumentNullException.ThrowIfNull(addToDownloadAsync);
        var admission = await ensureAdmissionAsync().ConfigureAwait(false);
        if (admission != DownloadAdmissionOutcome.Allowed)
        {
            return new DownloadAddFlowResult<TResult>(admission switch
            {
                DownloadAdmissionOutcome.Canceled => DownloadAddFlowOutcome.AdmissionCanceled,
                DownloadAdmissionOutcome.Failed => DownloadAddFlowOutcome.AdmissionFailed,
                _ => throw new InvalidOperationException($"Unsupported admission outcome: {admission:G}.")
            }, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var selectionResult = await selectDownloadAsync().ConfigureAwait(false);
        if (selectionResult.Outcome != DownloadSelectionOutcome.Accepted)
        {
            return new DownloadAddFlowResult<TResult>(selectionResult.Outcome switch
            {
                DownloadSelectionOutcome.Canceled => DownloadAddFlowOutcome.SelectionCanceled,
                DownloadSelectionOutcome.Unavailable => DownloadAddFlowOutcome.SelectionUnavailable,
                _ => throw new InvalidOperationException(
                    $"Unsupported selection outcome: {selectionResult.Outcome:G}.")
            }, null);
        }

        var selection = selectionResult.Selection
            ?? throw new InvalidOperationException("An accepted download selection requires a value.");
        cancellationToken.ThrowIfCancellationRequested();
        var result = await addToDownloadAsync(selection).ConfigureAwait(false);
        return new DownloadAddFlowResult<TResult>(DownloadAddFlowOutcome.Completed, result);
    }
}
