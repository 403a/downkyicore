using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Downloads;
using DownKyi.Presentation;
using DownKyi.Services.Download;

namespace DownKyi.Services.Video;

internal interface IVideoDetailDownloadCoordinator
{
    Task<int?> AddAsync(
        string input,
        VideoInfoView videoInfoView,
        IList<VideoSection> videoSections,
        bool isAll,
        CancellationToken cancellationToken);
}

internal sealed class VideoDetailDownloadCoordinator : IVideoDetailDownloadCoordinator
{
    private readonly IAddToDownloadServiceFactory _serviceFactory;
    private readonly DownloadContentConflictResolver _contentConflictResolver;

    public VideoDetailDownloadCoordinator(
        IAddToDownloadServiceFactory serviceFactory,
        DownloadContentConflictResolver contentConflictResolver)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _contentConflictResolver = contentConflictResolver
            ?? throw new ArgumentNullException(nameof(contentConflictResolver));
    }

    public Task<int?> AddAsync(
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
            return Task.FromResult<int?>(null);
        }

        var addService = _serviceFactory.Create(streamType.Value);
        return DownloadAddCoordinator.AddToDownloadIfSelectionAcceptedAsync(
            () => addService.EnsureAdmissionAsync(cancellationToken),
            () => addService.SelectDownloadAsync(cancellationToken),
            async selection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var preparedDownload = await addService
                    .PrepareAsync(videoInfoView, videoSections, isAll, cancellationToken)
                    .ConfigureAwait(false);
                var finalizedDownload = await _contentConflictResolver
                    .ResolveAsync(
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
            cancellationToken);
    }
}
