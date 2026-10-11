using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Bilibili;
using DownKyi.Application.Media;
using DownKyi.Core.BiliApi.Sign;
using DownKyi.Core.BiliApi.VideoStream.Models;
using DownKyi.Core.Settings;
using DownKyi.Presentation;

namespace DownKyi.Services.Video;

internal sealed class VideoParseCoordinator
{
    private readonly Func<string, CancellationToken, Task<IInfoService?>> _serviceFactory;
    private readonly object _serviceSync = new();
    private IInfoService? _infoService;
    private string? _infoServiceInput;

    public VideoParseCoordinator(
        ISettingsStore settingsStore,
        IVideoTagProvider tagProvider,
        IWbiKeyProvider wbiKeyProvider,
        IBilibiliApiClient client)
        : this((input, cancellationToken) =>
            CreateInfoServiceAsync(
                input,
                settingsStore,
                tagProvider,
                wbiKeyProvider,
                client,
                cancellationToken))
    {
    }

    internal VideoParseCoordinator(Func<string, CancellationToken, IInfoService?> serviceFactory)
        : this((input, cancellationToken) =>
            Task.FromResult(serviceFactory(input, cancellationToken)))
    {
    }

    internal VideoParseCoordinator(Func<string, CancellationToken, Task<IInfoService?>> serviceFactory)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
    }

    private async Task<IInfoService?> AcquireInfoServiceAsync(
        string input,
        bool refresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!refresh)
        {
            lock (_serviceSync)
            {
                if (_infoService != null && string.Equals(_infoServiceInput, input, StringComparison.Ordinal))
                {
                    return _infoService;
                }
            }
        }

        return await _serviceFactory(input, cancellationToken).ConfigureAwait(false);
    }

    public void Reset()
    {
        lock (_serviceSync)
        {
            _infoService = null;
            _infoServiceInput = null;
        }
    }

    public async Task<VideoDetailParseResult> LoadDetailAsync(
        string input,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var service = await AcquireInfoServiceAsync(input, refresh, cancellationToken).ConfigureAwait(false);
        if (service == null)
        {
            return VideoDetailParseResult.Empty;
        }

        var videoInfoView = service.GetVideoView(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (videoInfoView == null)
        {
            return VideoDetailParseResult.Empty;
        }

        var videoSections = service.GetVideoSections(false, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (videoSections != null)
        {
            var result = VideoDetailParseResult.From(videoInfoView, videoSections);
            CommitInfoService(input, service, cancellationToken);
            return result;
        }

        var pages = service.GetVideoPages(cancellationToken) ?? new List<VideoPage>();
        cancellationToken.ThrowIfCancellationRequested();
        VideoSection[] defaultSections =
        [
            new VideoSection
            {
                Id = 0,
                Title = "default",
                IsSelected = true,
                VideoPages = pages
            }
        ];
        var defaultResult = VideoDetailParseResult.From(videoInfoView, defaultSections);
        CommitInfoService(input, service, cancellationToken);
        return defaultResult;
    }

    public async Task<VideoStreamParseResult> LoadPageStreamAsync(
        string input,
        VideoPage page,
        bool refresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var service = await AcquireInfoServiceAsync(input, refresh, cancellationToken).ConfigureAwait(false);
        if (service == null)
        {
            return VideoStreamParseResult.Unavailable(
                page,
                VideoStreamUnavailableReason.ParserUnavailable);
        }

        var playUrl = await service.GetVideoStreamAsync(page, cancellationToken).ConfigureAwait(false);
        var result = VideoStreamParseResult.From(page, playUrl);
        CommitInfoService(input, service, cancellationToken);
        return result;
    }

    public async Task<VideoStreamParseBatchResult> LoadPageStreamsAsync(
        string input,
        IReadOnlyList<VideoPage> pages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
        {
            return VideoStreamParseBatchResult.NoPagesSelected;
        }

        var service = await AcquireInfoServiceAsync(input, refresh: false, cancellationToken).ConfigureAwait(false);
        if (service == null)
        {
            return VideoStreamParseBatchResult.From(pages.Select(page =>
                VideoStreamParseResult.Unavailable(
                    page,
                    VideoStreamUnavailableReason.ParserUnavailable)));
        }

        var results = new List<VideoStreamParseResult>(pages.Count);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playUrl = await service.GetVideoStreamAsync(page, cancellationToken).ConfigureAwait(false);
            results.Add(VideoStreamParseResult.From(page, playUrl));
        }

        CommitInfoService(input, service, cancellationToken);
        return VideoStreamParseBatchResult.From(results);
    }

    private void CommitInfoService(
        string input,
        IInfoService infoService,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_serviceSync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _infoService = infoService;
            _infoServiceInput = input;
        }
    }

    internal static async Task<IInfoService?> CreateInfoServiceAsync(
        string input,
        ISettingsStore settingsStore,
        IVideoTagProvider tagProvider,
        IWbiKeyProvider wbiKeyProvider,
        IBilibiliApiClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(tagProvider);
        ArgumentNullException.ThrowIfNull(wbiKeyProvider);
        ArgumentNullException.ThrowIfNull(client);
        var kind = VideoInputResolver.Resolve(input);
        if (kind == VideoInputKind.Video)
        {
            return await VideoInfoService.CreateAsync(
                input,
                settingsStore,
                tagProvider,
                wbiKeyProvider,
                client,
                cancellationToken).ConfigureAwait(false);
        }

        return kind switch
        {
            VideoInputKind.Bangumi => await BangumiInfoService.CreateAsync(
                input,
                settingsStore,
                client,
                cancellationToken).ConfigureAwait(false),
            VideoInputKind.Cheese => await CheeseInfoService.CreateAsync(
                input,
                settingsStore,
                client,
                cancellationToken).ConfigureAwait(false),
            _ => null
        };
    }
}

internal enum VideoDetailParseOutcome
{
    NoContent,
    Ready
}

internal sealed record VideoDetailParseResult
{
    private VideoDetailParseResult(
        VideoDetailParseOutcome outcome,
        VideoInfoView? videoInfoView,
        IReadOnlyList<VideoSection> videoSections)
    {
        Outcome = outcome;
        VideoInfoView = videoInfoView;
        VideoSections = videoSections;
    }

    public VideoDetailParseOutcome Outcome { get; }

    public VideoInfoView? VideoInfoView { get; }

    public IReadOnlyList<VideoSection> VideoSections { get; }

    public static VideoDetailParseResult Empty { get; } = new(
        VideoDetailParseOutcome.NoContent,
        null,
        Array.Empty<VideoSection>());

    public static VideoDetailParseResult From(
        VideoInfoView videoInfoView,
        IEnumerable<VideoSection> videoSections)
    {
        ArgumentNullException.ThrowIfNull(videoInfoView);
        ArgumentNullException.ThrowIfNull(videoSections);
        var sections = videoSections.ToArray();
        return sections.Any(section => section.VideoPages.Count > 0)
            ? new VideoDetailParseResult(VideoDetailParseOutcome.Ready, videoInfoView, sections)
            : Empty;
    }
}

internal enum VideoStreamUnavailableReason
{
    ParserUnavailable,
    NoPlayableMedia
}

internal sealed record VideoStreamParseResult(
    VideoPage Page,
    PlayUrl? PlayUrl,
    VideoStreamUnavailableReason? UnavailableReason)
{
    public bool HasUsableContent => PlayUrl != null && UnavailableReason == null;

    public static VideoStreamParseResult From(VideoPage page, PlayUrl? playUrl)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (playUrl == null)
        {
            return Unavailable(page, VideoStreamUnavailableReason.NoPlayableMedia);
        }

        var availability = playUrl.Availability ?? PlayUrlAvailability.From(playUrl);
        return availability.HasPlayableMedia
            ? new VideoStreamParseResult(page, playUrl, null)
            : Unavailable(page, VideoStreamUnavailableReason.NoPlayableMedia);
    }

    public static VideoStreamParseResult Unavailable(
        VideoPage page,
        VideoStreamUnavailableReason reason) =>
        new(page ?? throw new ArgumentNullException(nameof(page)), null, reason);
}

internal sealed record VideoStreamParseBatchResult(
    IReadOnlyList<VideoStreamParseResult> Items,
    bool HadSelection)
{
    public static VideoStreamParseBatchResult NoPagesSelected { get; } =
        new(Array.Empty<VideoStreamParseResult>(), HadSelection: false);

    public int SuccessfulCount => Items.Count(item => item.HasUsableContent);

    public int UnavailableCount => Items.Count - SuccessfulCount;

    public static VideoStreamParseBatchResult From(IEnumerable<VideoStreamParseResult> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new VideoStreamParseBatchResult(items.ToArray(), HadSelection: true);
    }

    public static VideoStreamParseBatchResult From(VideoStreamParseResult item) =>
        new([item ?? throw new ArgumentNullException(nameof(item))], HadSelection: true);
}
