namespace DownKyi.Application.Downloads;

public enum DownloadAdmissionOutcome
{
    Allowed,
    Canceled,
    Failed
}

public enum DownloadSelectionOutcome
{
    Accepted,
    Canceled,
    Unavailable
}

public sealed record DownloadSelectionResult
{
    private DownloadSelectionResult(
        DownloadSelectionOutcome outcome,
        DownloadAddSelection? selection)
    {
        Outcome = outcome;
        Selection = selection;
    }

    public DownloadSelectionOutcome Outcome { get; }

    public DownloadAddSelection? Selection { get; }

    public static DownloadSelectionResult Accepted(DownloadAddSelection selection) =>
        new(
            DownloadSelectionOutcome.Accepted,
            selection ?? throw new ArgumentNullException(nameof(selection)));

    public static DownloadSelectionResult Canceled { get; } =
        new(DownloadSelectionOutcome.Canceled, null);

    public static DownloadSelectionResult Unavailable { get; } =
        new(DownloadSelectionOutcome.Unavailable, null);
}

public enum DownloadAddFlowOutcome
{
    Completed,
    AdmissionCanceled,
    AdmissionFailed,
    SelectionCanceled,
    SelectionUnavailable
}

public sealed record DownloadAddFlowResult<TResult>
    where TResult : struct
{
    internal DownloadAddFlowResult(
        DownloadAddFlowOutcome outcome,
        TResult? result)
    {
        if ((outcome == DownloadAddFlowOutcome.Completed) != result.HasValue)
        {
            throw new ArgumentException(
                "Only a completed download flow can contain a result.",
                nameof(result));
        }

        Outcome = outcome;
        Value = result;
    }

    public DownloadAddFlowOutcome Outcome { get; }

    public TResult? Value { get; }

}
