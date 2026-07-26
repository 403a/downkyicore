using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Desktop;
using DownKyi.Core.BiliApi.BiliUtils;
using DownKyi.Core.BiliApi.Sign;
using DownKyi.Core.BiliApi.VideoStream;
using DownKyi.Core.BiliApi.VideoStream.Models;
using DownKyi.Core.Logging;
using DownKyi.Domain.Results;
using Microsoft.Extensions.Logging;

namespace DownKyi.Services.Download;

internal sealed class ResolvePlaybackStage : IDownloadPipelineStage
{
    private readonly IUserNotificationService _notificationService;
    private readonly DownloadActivityPresenter _presenter;
    private readonly IWbiKeyProvider _wbiKeyProvider;
    private readonly ILogger _logger;

    public ResolvePlaybackStage(
        IUserNotificationService notificationService,
        DownloadActivityPresenter presenter,
        IWbiKeyProvider wbiKeyProvider,
        ILogger logger)
    {
        _notificationService = notificationService
            ?? throw new ArgumentNullException(nameof(notificationService));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _wbiKeyProvider = wbiKeyProvider ?? throw new ArgumentNullException(nameof(wbiKeyProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => nameof(ResolvePlaybackStage);

    public async Task<OperationResult<DownloadStageResult>> ExecuteAsync(
        DownloadExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var downloading = context.Downloading;
        downloading.DownloadBase.FilePath = downloading.DownloadBase.FilePath
            .Replace("\\", "/", StringComparison.Ordinal);

        string path;
        try
        {
            path = GetDownloadDirectoryPath(downloading.DownloadBase.FilePath);
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            _logger.LogWarningMessage("Download directory could not be prepared.", exception);
            _notificationService.Show(DownloadActivityPresenter.CreateDirectoryError(
                Path.GetDirectoryName(downloading.DownloadBase.FilePath) ?? string.Empty));
            return DownloadStageResult.Failure(
                "download.resolve.directory",
                "Download directory could not be prepared.");
        }

        context.DownloadDirectory = path;
        DownloadActivityPresenter.Reset(downloading);
        await _presenter.ShowParsingAsync(context, cancellationToken).ConfigureAwait(true);

        if (downloading.PlayUrl != null)
        {
            return DownloadStageResult.Success(Name);
        }

        var playUrl = await ResolvePlayUrlAsync(context, cancellationToken).ConfigureAwait(true);
        if (playUrl == null)
        {
            return DownloadStageResult.Failure(
                "download.resolve.playback",
                "Playback data could not be resolved.");
        }

        downloading.PlayUrl = playUrl;
        return DownloadStageResult.Success(Name);
    }

    internal static string GetDownloadDirectoryPath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Path.GetDirectoryName(filePath)
               ?? throw new ArgumentException(
                   "Download file path must include a directory.",
                   nameof(filePath));
    }

    private async Task<PlayUrl?> ResolvePlayUrlAsync(
        DownloadExecutionContext context,
        CancellationToken cancellationToken)
    {
        var downloading = context.Downloading;
        return downloading.Downloading.PlayStreamType switch
        {
            PlayStreamType.Video => await WbiRequestExecutor.ExecuteAsync(
                _wbiKeyProvider,
                (keys, unixTimeSeconds) => context.Settings.Video.VideoParseType switch
                {
                    0 => VideoStreamApi.GetVideoPlayUrl(
                        keys,
                        unixTimeSeconds,
                        downloading.DownloadBase.Avid,
                        downloading.DownloadBase.Bvid,
                        downloading.DownloadBase.Cid,
                        cancellationToken: cancellationToken),
                    1 => VideoStreamApi.GetVideoPlayUrlWebPage(
                        keys,
                        unixTimeSeconds,
                        downloading.DownloadBase.Avid,
                        downloading.DownloadBase.Bvid,
                        downloading.DownloadBase.Cid,
                        downloading.DownloadBase.Page,
                        cancellationToken),
                    _ => throw new ArgumentException(
                        "Invalid video parse type. Valid values are: 0 (WebAPI) or 1 (WebPage).")
                },
                TimeProvider.System,
                cancellationToken).ConfigureAwait(true),
            PlayStreamType.Bangumi => VideoStreamApi.GetBangumiPlayUrl(
                downloading.DownloadBase.Avid,
                downloading.DownloadBase.Bvid,
                downloading.DownloadBase.Cid,
                cancellationToken: cancellationToken),
            PlayStreamType.Cheese => VideoStreamApi.GetCheesePlayUrl(
                downloading.DownloadBase.Avid,
                downloading.DownloadBase.Bvid,
                downloading.DownloadBase.Cid,
                downloading.DownloadBase.EpisodeId,
                cancellationToken: cancellationToken),
            _ => null
        };
    }
}
