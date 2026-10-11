using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Bilibili;
using DownKyi.Application.Diagnostics;
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
        var preferences = _settingsStore.Current.Video;
        // An already parsed page may have been discovered for different checkboxes.
        // Re-query only for requested media still missing or below the preference.
        var requireVideo = requestedContent.Video
                           && (page.VideoQualityMatch != null || page.VideoQuality == null);
        var requireAudio = requestedContent.Audio
                           && (page.AudioQualityMatch != null
                               || string.IsNullOrEmpty(page.AudioQualityFormat));
        if (page.PlaybackAvailability == null
            || PlaybackDiscoveryPolicy.NeedsApiSupplement(
                page.PlaybackAvailability,
                preferences.Quality,
                preferences.AudioQuality,
                requireVideo,
                requireAudio))
        {
            // Preserve explicit per-page choices when enriching cached discovery.
            var selectedVideo = page.VideoQualityMatch == null ? page.VideoQuality : null;
            var selectedAudio = page.AudioQualityMatch == null ? page.AudioQualityFormat : null;
            var playUrl = await videoInfoService
                .GetVideoStreamAsync(
                    page,
                    requestedContent.Video,
                    requestedContent.Audio,
                    cancellationToken)
                .ConfigureAwait(false);
            VideoPagePlaybackMapper.ApplyPlayUrl(
                playUrl,
                page,
                _settingsStore.Current,
                _logger);

            if (selectedVideo != null)
            {
                var matchingVideo = page.VideoQualityList.FirstOrDefault(candidate =>
                    candidate.Quality == selectedVideo.Quality
                    && candidate.IsDurl == selectedVideo.IsDurl);
                if (matchingVideo != null
                    && matchingVideo.VideoCodecList.Contains(selectedVideo.SelectedVideoCodec))
                {
                    matchingVideo.SelectedVideoCodec = selectedVideo.SelectedVideoCodec;
                    page.VideoQuality = matchingVideo;
                }
            }

            if (!string.IsNullOrEmpty(selectedAudio)
                && page.AudioQualityFormatList.Contains(selectedAudio))
            {
                page.AudioQualityFormat = selectedAudio;
            }
        }

    }
}
