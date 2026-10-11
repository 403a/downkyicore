using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Bilibili;
using DownKyi.Application.Diagnostics;
using DownKyi.Core.BiliApi.BiliUtils;
using DownKyi.Core.BiliApi.Sign;
using DownKyi.Core.BiliApi.VideoStream;
using DownKyi.Core.Settings;
using DownKyi.Domain.Downloads;
using DownKyi.Presentation;
using DownKyi.Services.Video;
using Microsoft.Extensions.Logging;

namespace DownKyi.Services.Download;

internal sealed class DownloadPreparationService
{
    private readonly IInfoService _playbackService;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger _logger;

    public DownloadPreparationService(
        PlayStreamType streamType,
        ISettingsStore settingsStore,
        IVideoTagProvider tagProvider,
        IWbiKeyProvider wbiKeyProvider,
        IBilibiliApiClient client,
        ILogger logger)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(tagProvider);
        ArgumentNullException.ThrowIfNull(wbiKeyProvider);
        ArgumentNullException.ThrowIfNull(client);
        _playbackService = streamType switch
        {
            PlayStreamType.Video => new VideoInfoService(
                settingsStore,
                tagProvider,
                wbiKeyProvider,
                client),
            PlayStreamType.Bangumi => new BangumiInfoService(settingsStore, client),
            PlayStreamType.Cheese => new CheeseInfoService(settingsStore, client),
            _ => throw new ArgumentOutOfRangeException(nameof(streamType), streamType, null)
        };
    }

    public async Task<PreparedDownload> PrepareAsync(
        VideoInfoView videoInfoView,
        IList<VideoSection> videoSections,
        DownloadContentSelection requestedContent,
        bool isAll,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(videoInfoView);
        ArgumentNullException.ThrowIfNull(videoSections);
        ArgumentNullException.ThrowIfNull(requestedContent);
        foreach (var section in videoSections)
        {
            foreach (var page in section.VideoPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((isAll || page.IsSelected) && requestedContent.HasMedia)
                {
                    await PrepareMediaAsync(
                        _playbackService,
                        page,
                        requestedContent,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return PreparedDownload.Create(videoInfoView, videoSections);
    }

    public async Task<PreparedDownload?> PrepareAsync(
        IInfoService videoInfoService,
        DownloadContentSelection requestedContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(videoInfoService);
        ArgumentNullException.ThrowIfNull(requestedContent);
        cancellationToken.ThrowIfCancellationRequested();
        var videoInfoView = videoInfoService.GetVideoView(cancellationToken);
        if (videoInfoView == null)
        {
            _logger.LogDebugMessage("VideoInfoView is null.");
            return null;
        }

        var videoSections = videoInfoService.GetVideoSections(true, cancellationToken);
        if (videoSections == null)
        {
            _logger.LogDebugMessage("Video sections do not exist.");
            videoSections =
            [
                new VideoSection
                {
                    Id = 0,
                    Title = "default",
                    IsSelected = true,
                    VideoPages = videoInfoService.GetVideoPages(cancellationToken) ?? []
                }
            ];
        }

        foreach (var section in videoSections)
        {
            foreach (var page in section.VideoPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                page.IsSelected = true;
                if (requestedContent.HasMedia)
                {
                    await PrepareMediaAsync(
                        videoInfoService,
                        page,
                        requestedContent,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return PreparedDownload.Create(videoInfoView, videoSections);
    }

    private async Task PrepareMediaAsync(
        IInfoService videoInfoService,
        VideoPage page,
        DownloadContentSelection requestedContent,
        CancellationToken cancellationToken)
    {
        var settings = _settingsStore.Current;
        var request = CreateDiscoveryRequest(page, requestedContent, settings);
        var currentAvailability = page.PlaybackAvailability;
        if (currentAvailability == null
            || request.NeedsApiSupplement(currentAvailability))
        {
            var playUrl = await videoInfoService
                .GetVideoStreamAsync(
                    page,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            VideoPagePlaybackMapper.ApplyPlayUrlPreservingSelection(
                playUrl,
                page,
                settings,
                _logger);
        }
    }

    private static PlaybackDiscoveryRequest CreateDiscoveryRequest(
        VideoPage page,
        DownloadContentSelection requestedContent,
        ApplicationSettings settings)
    {
        var videoSettings = settings.Video;
        var hasCachedAvailability = page.PlaybackAvailability != null;
        var preferredVideoQuality = hasCachedAvailability
                                    && page.VideoQualityMatch == null
                                    && page.VideoQuality is { Quality: > 0 } selectedVideo
            ? selectedVideo.Quality
            : videoSettings.Quality;
        var preferredAudioQuality = hasCachedAvailability
                                    && page.AudioQualityMatch == null
                                    && !string.IsNullOrEmpty(page.AudioQualityFormat)
            ? PlaybackQualityCatalog.GetAudioQualities()
                  .FirstOrDefault(quality => string.Equals(
                      quality.Name,
                      page.AudioQualityFormat,
                      StringComparison.Ordinal))
                  ?.Id
              ?? videoSettings.AudioQuality
            : videoSettings.AudioQuality;
        return new PlaybackDiscoveryRequest(
            preferredVideoQuality,
            preferredAudioQuality,
            requestedContent.Video,
            requestedContent.Audio);
    }
}
