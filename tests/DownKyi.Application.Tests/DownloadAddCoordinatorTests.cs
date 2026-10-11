using DownKyi.Application.Downloads;
using DownKyi.Domain.Downloads;

namespace DownKyi.Application.Tests;

public sealed class DownloadAddCoordinatorTests
{
    [Fact]
    public async Task CancelingDirectorySelectionDoesNotAddADownload()
    {
        var addWasCalled = false;

        var result = await DownloadAddCoordinator.AddToDownloadIfSelectionAcceptedAsync(
            () => Task.FromResult(DownloadAdmissionOutcome.Allowed),
            () => Task.FromResult(DownloadSelectionResult.Canceled),
            _ =>
            {
                addWasCalled = true;
                return Task.FromResult(1);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(DownloadAddFlowOutcome.SelectionCanceled, result.Outcome);
        Assert.Null(result.Value);
        Assert.False(addWasCalled);
    }

    [Fact]
    public async Task AcceptedSelectionReachesTheDownloadOperation()
    {
        DownloadAddSelection? receivedSelection = null;
        var requestedContent = DownloadContentSelection.None with { Video = true };

        var result = await DownloadAddCoordinator.AddToDownloadIfSelectionAcceptedAsync(
            () => Task.FromResult(DownloadAdmissionOutcome.Allowed),
            () => Task.FromResult(DownloadSelectionResult.Accepted(
                new DownloadAddSelection("D:\\Downloads", requestedContent))),
            selection =>
            {
                receivedSelection = selection;
                return Task.FromResult(2);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(DownloadAddFlowOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.Value);
        Assert.Equal("D:\\Downloads", receivedSelection!.Directory);
        Assert.Same(requestedContent, receivedSelection.RequestedContent);
    }

    [Fact]
    public async Task BlockedAdmissionStopsBeforeDirectorySelectionAndBatchAdd()
    {
        var directorySelectionCount = 0;
        var addCount = 0;

        var result = await DownloadAddCoordinator.AddToDownloadIfSelectionAcceptedAsync(
            () => Task.FromResult(DownloadAdmissionOutcome.Canceled),
            () =>
            {
                directorySelectionCount++;
                return Task.FromResult(DownloadSelectionResult.Accepted(
                    new DownloadAddSelection(
                        "D:\\Downloads",
                        DownloadContentSelection.All)));
            },
            _ =>
            {
                addCount++;
                return Task.FromResult(2);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(DownloadAddFlowOutcome.AdmissionCanceled, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal(0, directorySelectionCount);
        Assert.Equal(0, addCount);
    }
}
