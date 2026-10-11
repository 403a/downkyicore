namespace DownKyi.Services.Download;

internal enum DownloadAddStopReason
{
    UnsupportedInput,
    AdmissionCanceled,
    AdmissionFailed,
    SelectionCanceled,
    SelectionUnavailable,
    NoContentRequested,
    NoPagesSelected,
    NoAvailableContent,
    SkippedByUser,
    RuntimeUnavailable,
    TaskAdmissionFailed,
    CancellationRequested
}
