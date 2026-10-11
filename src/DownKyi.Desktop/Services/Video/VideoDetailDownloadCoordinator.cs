using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Downloads;
using DownKyi.Presentation;
using DownKyi.Services.Download;

namespace DownKyi.Services.Video;

internal interface IVideoDetailDownloadCoordinator
{
    Task<DownloadAddResult> AddAsync(
        string input,
        VideoInfoView videoInfoView,
        IList<VideoSection> videoSections,
        bool isAll,
        CancellationToken cancellationToken);
}

internal sealed class VideoDetailDownloadCoordinator : IVideoDetailDownloadCoordinator
{
    private readonly IAddToDownloadServiceFactory _serviceFactory;
    private readonly DownloadActionPlanner _actionPlanner;

    public VideoDetailDownloadCoordinator(
        IAddToDownloadServiceFactory serviceFactory,
        DownloadActionPlanner actionPlanner)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _actionPlanner = actionPlanner ?? throw new ArgumentNullException(nameof(actionPlanner));
    }

    public async Task<DownloadAddResult> AddAsync(
        string input,
        VideoInfoView videoInfoView,
        IList<VideoSection> videoSections,
        bool isAll,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(videoInfoView);
        ArgumentNullException.ThrowIfNull(videoSections);
        cancellationToken.ThrowIfCancellationRequested();

        var streamType = PlayStreamTypeResolver.ResolvePlayStreamType(input);
        if (streamType == null)
        {
            return DownloadAddResult.Stopped(
                DownloadAddOutcome.Rejected,
                DownloadAddStopReason.UnsupportedInput);
        }

        var addService = _serviceFactory.Create(streamType.Value);
        var selectedPages = videoSections.SelectMany(section => section.VideoPages)
            .Where(page => isAll || page.IsSelected)
            .Take(2)
            .ToArray();
        var flow = await DownloadAddCoordinator.AddToDownloadIfSelectionAcceptedAsync(
            () => addService.EnsureAdmissionAsync(cancellationToken),
            () => addService.SelectDownloadAsync(
                selectedPages.Length == 1 ? selectedPages[0] : null,
                cancellationToken),
            async selection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var preparedDownload = await addService
                    .PrepareAsync(
                        videoInfoView,
                        videoSections,
                        selection.RequestedContent,
                        isAll,
                        cancellationToken)
                    .ConfigureAwait(false);
                var finalizedDownload = await _actionPlanner
                    .PlanAsync(
                        selection.RequestedContent,
                        preparedDownload,
                        isAll,
                        new DownloadContentConflictChoices(),
                        cancellationToken)
                    .ConfigureAwait(true);
                return await addService
                    .AddToDownload(selection.Directory, finalizedDownload, cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(true);
        return flow.Outcome switch
        {
            DownloadAddFlowOutcome.Completed => flow.Value
                ?? throw new InvalidOperationException("A completed download operation requires a result."),
            DownloadAddFlowOutcome.AdmissionCanceled => DownloadAddResult.Stopped(
                DownloadAddOutcome.Canceled,
                DownloadAddStopReason.AdmissionCanceled),
            DownloadAddFlowOutcome.AdmissionFailed => DownloadAddResult.Stopped(
                DownloadAddOutcome.Failed,
                DownloadAddStopReason.AdmissionFailed),
            DownloadAddFlowOutcome.SelectionCanceled => DownloadAddResult.Stopped(
                DownloadAddOutcome.Canceled,
                DownloadAddStopReason.SelectionCanceled),
            DownloadAddFlowOutcome.SelectionUnavailable => DownloadAddResult.Stopped(
                DownloadAddOutcome.Failed,
                DownloadAddStopReason.SelectionUnavailable),
            _ => throw new InvalidOperationException(
                $"Unsupported download flow outcome: {flow.Outcome:G}.")
        };
    }
}
