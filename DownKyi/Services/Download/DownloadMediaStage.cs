using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Core.BiliApi.BiliUtils;
using DownKyi.Core.BiliApi.VideoStream.Models;
using DownKyi.Core.Logging;
using DownKyi.Core.Settings;
using DownKyi.Domain.Results;
using Microsoft.Extensions.Logging;

namespace DownKyi.Services.Download;

internal sealed class DownloadMediaStage : IDownloadPipelineStage
{
    private const int RetryLimit = 5;

    private readonly DownloadTaskProjectionStore _projectionStore;
    private readonly DownloadTaskStateWriter _stateWriter;
    private readonly ITransferBackend _transferBackend;
    private readonly ILogger _logger;

    public DownloadMediaStage(
        DownloadTaskProjectionStore projectionStore,
        DownloadTaskStateWriter stateWriter,
        ITransferBackend transferBackend,
        ILogger logger)
    {
        _projectionStore = projectionStore
            ?? throw new ArgumentNullException(nameof(projectionStore));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _transferBackend = transferBackend ?? throw new ArgumentNullException(nameof(transferBackend));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => nameof(DownloadMediaStage);

    public async Task<OperationResult<DownloadStageResult>> ExecuteAsync(
        DownloadExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.EnsureActive(cancellationToken);
        var playUrl = context.Downloading.PlayUrl;
        context.MediaKind = DetectMediaKind(playUrl);
        if (context.MediaKind == DownloadMediaKind.Dash)
        {
            return await DownloadDashAsync(context, cancellationToken).ConfigureAwait(true);
        }

        if (context.MediaKind == DownloadMediaKind.Durl)
        {
            return await DownloadDurlsAsync(
                context,
                playUrl.Durl,
                cancellationToken).ConfigureAwait(true);
        }

        return DownloadStageResult.Failure(
            "download.media.missing",
            "Playback data does not contain a supported media stream.");
    }

    internal static DownloadMediaKind DetectMediaKind(PlayUrl? playUrl)
    {
        if (playUrl?.Dash is { } dash &&
            (dash.Video.Count > 0 || dash.Audio.Count > 0))
        {
            return DownloadMediaKind.Dash;
        }

        return playUrl?.Durl.Count > 0
            ? DownloadMediaKind.Durl
            : DownloadMediaKind.None;
    }

    internal static PlayUrlDashVideo? CreateDurlDownloadDescriptor(
        IEnumerable<PlayUrlDurl> durls)
    {
        ArgumentNullException.ThrowIfNull(durls);
        var durl = durls.OrderBy(item => item.Order).FirstOrDefault();
        return durl == null
            ? null
            : new PlayUrlDashVideo
            {
                BackupUrl = durl.BackupUrl,
                BaseAddress = durl.SourceAddress,
                Codecs = "durl",
                Id = durl.Order,
                ExpectedSize = durl.Size
            };
    }

    private async Task<OperationResult<DownloadStageResult>> DownloadDashAsync(
        DownloadExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.NeedsAudio)
        {
            DownloadActivityPresenter.ShowDownloadingAudio(context.Downloading);
            var result = await DownloadWithRetryAsync(
                context,
                SelectAudio(context),
                cancellationToken).ConfigureAwait(true);
            if (!result.TryGetValue(out var audioFile))
            {
                return DownloadStageResult.Failure(
                    result.Error?.Code ?? "download.media.audio",
                    result.Error?.Message ?? "Audio transfer failed.");
            }

            context.AudioFile = audioFile;
        }

        context.EnsureActive(cancellationToken);
        if (context.NeedsVideo)
        {
            DownloadActivityPresenter.ShowDownloadingVideo(context.Downloading);
            var result = await DownloadWithRetryAsync(
                context,
                SelectVideo(context),
                cancellationToken).ConfigureAwait(true);
            if (!result.TryGetValue(out var videoFile))
            {
                return DownloadStageResult.Failure(
                    result.Error?.Code ?? "download.media.video",
                    result.Error?.Message ?? "Video transfer failed.");
            }

            context.VideoFile = videoFile;
        }

        context.EnsureActive(cancellationToken);
        return DownloadStageResult.Success(Name);
    }

    private async Task<OperationResult<DownloadStageResult>> DownloadDurlsAsync(
        DownloadExecutionContext context,
        IEnumerable<PlayUrlDurl> source,
        CancellationToken cancellationToken)
    {
        if (!context.NeedsMedia)
        {
            context.EnsureActive(cancellationToken);
            return DownloadStageResult.Success(Name);
        }

        DownloadActivityPresenter.ShowDownloadingVideo(context.Downloading);
        var downloads = source
            .OrderBy(durl => durl.Order)
            .Select(durl => new PendingDurlDownload(durl))
            .ToArray();

        for (var retryCount = 0; retryCount < RetryLimit; retryCount++)
        {
            foreach (var download in downloads.Where(item => item.FilePath == null))
            {
                var result = await DownloadMediaFileAsync(
                    context,
                    CreateDurlDownloadDescriptor([download.Durl]),
                    cancellationToken).ConfigureAwait(true);
                if (result.TryGetValue(out var filePath))
                {
                    download.FilePath = filePath;
                }
            }

            if (downloads.All(download => download.FilePath != null))
            {
                context.DurlDownloads = downloads
                    .Select(download => new DurlDownloadResult(
                        download.Durl,
                        GetCompletedFilePath(download)))
                    .ToArray();
                context.EnsureActive(cancellationToken);
                return DownloadStageResult.Success(Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        }

        return DownloadStageResult.Failure(
            "download.media.durl",
            "One or more segmented media transfers failed.");
    }

    private static string GetCompletedFilePath(PendingDurlDownload download)
    {
        return download.FilePath
               ?? throw new InvalidOperationException(
                   "A completed DURL transfer must have a file path.");
    }

    private async Task<OperationResult<string>> DownloadWithRetryAsync(
        DownloadExecutionContext context,
        PlayUrlDashVideo? media,
        CancellationToken cancellationToken)
    {
        OperationResult<string>? lastFailure = null;
        for (var attempt = 0; attempt < RetryLimit; attempt++)
        {
            var result = await DownloadMediaFileAsync(
                context,
                media,
                cancellationToken).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                return result;
            }

            lastFailure = result;
        }

        return lastFailure ?? OperationResult.Failure<string>(
            OperationError.Unexpected(
                "download.media.descriptor",
                "The selected media stream is unavailable."));
    }

    private async Task<OperationResult<string>> DownloadMediaFileAsync(
        DownloadExecutionContext context,
        PlayUrlDashVideo? media,
        CancellationToken cancellationToken)
    {
        if (media == null)
        {
            return OperationResult.Failure<string>(OperationError.Unexpected(
                "download.media.descriptor",
                "The selected media stream is unavailable."));
        }

        context.EnsureActive(cancellationToken);
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(media.BaseAddress))
        {
            urls.Add(media.BaseAddress);
        }

        urls.AddRange(media.BackupUrl.Where(url => !string.IsNullOrWhiteSpace(url)));
        if (urls.Count == 0)
        {
            return OperationResult.Failure<string>(OperationError.Unexpected(
                "download.media.url",
                "The selected media stream has no usable address."));
        }

        var path = context.DownloadDirectory;
        if (string.IsNullOrWhiteSpace(path))
        {
            return OperationResult.Failure<string>(OperationError.Unexpected(
                "download.media.directory",
                "The download directory is unavailable."));
        }

        var fileName = Guid.NewGuid().ToString("N");
        var key = DownloadTransferKey.Create(media.Id, media.Codecs);
        var snapshot = _projectionStore.GetRequiredSnapshot(context.TaskId);
        if (snapshot.Plan.TransferFiles.TryGetValue(key, out var existingFileName))
        {
            fileName = existingFileName;
            var cachedFile = Path.Combine(path, fileName);
            if (snapshot.Transfer.CompletedFileKeys.Contains(key, StringComparer.Ordinal) &&
                IsDownloadedMediaFileUsable(cachedFile, media.ExpectedSize))
            {
                return OperationResult.Success(cachedFile);
            }

            if (snapshot.Transfer.CompletedFileKeys.Contains(key, StringComparer.Ordinal))
            {
                DeleteInvalidDownloadedMediaFile(cachedFile);
                await _stateWriter.InvalidateCompletedFileAsync(
                    context.TaskId,
                    key,
                    cancellationToken).ConfigureAwait(true);
            }
        }
        else
        {
            await _stateWriter.RecordTransferFileAsync(
                context.TaskId,
                key,
                fileName,
                cancellationToken).ConfigureAwait(true);
        }

        NormalizeTransferSchemes(
            urls,
            context.Settings.Network.UseSsl == AllowStatus.Yes);
        var targetFile = Path.Combine(path, fileName);
        var outcome = await _transferBackend.TransferAsync(
            DownloadTransferRequestFactory.Create(
                context.TaskId,
                urls,
                path,
                fileName,
                media.ExpectedSize,
                _projectionStore,
                _stateWriter,
                () => context.EnsureActive(cancellationToken),
                cancellationToken)).ConfigureAwait(true);
        if (outcome == DownloadTransferOutcome.Succeeded &&
            IsDownloadedMediaFileUsable(targetFile, media.ExpectedSize))
        {
            await _stateWriter.CompleteTransferFileAsync(
                context.TaskId,
                key,
                cancellationToken).ConfigureAwait(true);
            return OperationResult.Success(targetFile);
        }

        if (outcome == DownloadTransferOutcome.Paused)
        {
            throw new OperationCanceledException("Download was paused.");
        }

        DeleteInvalidDownloadedMediaFile(targetFile);
        await _stateWriter.SetBackendIdentityAsync(
            context.TaskId,
            null,
            cancellationToken).ConfigureAwait(true);
        return OperationResult.Failure<string>(OperationError.Unexpected(
            "download.media.transfer",
            "Media transfer did not produce a valid file."));
    }

    private static PlayUrlDashVideo? SelectAudio(DownloadExecutionContext context)
    {
        var downloading = context.Downloading;
        var dash = downloading.PlayUrl?.Dash;
        if (dash?.Audio is not { Count: > 0 } audio)
        {
            return null;
        }

        var selected = audio.FirstOrDefault(item => item.Id == downloading.AudioCodec.Id);
        if (downloading.AudioCodec.Id == 30250 &&
            dash.Dolby?.Audio is { Count: > 0 } dolbyAudio)
        {
            selected = dolbyAudio[0];
        }

        if (downloading.AudioCodec.Id == 30251 && dash.Flac?.Audio is { } flacAudio)
        {
            selected = flacAudio;
        }

        return selected;
    }

    internal static PlayUrlDashVideo? SelectVideo(DownloadExecutionContext context)
    {
        var downloading = context.Downloading;
        var video = downloading.PlayUrl?.Dash?.Video?.FirstOrDefault(item =>
        {
            var codec = Constant.GetCodecIds().FirstOrDefault(candidate =>
                candidate.Id == item.CodecId);
            return item.Id == downloading.Resolution.Id &&
                   codec?.Name == downloading.VideoCodecName;
        });
        if (video == null)
        {
            return null;
        }

        return video;
    }

    private bool IsDownloadedMediaFileUsable(
        string? file,
        long expectedBytes = 0)
    {
        var result = DownloadFileIntegrity.Check(file, expectedBytes);
        if (!result.IsUsable)
        {
            _logger.LogInformationMessage(
                result.Reason ?? "Downloaded media file is not usable.");
        }

        return result.IsUsable;
    }

    private void DeleteInvalidDownloadedMediaFile(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        foreach (var path in new[] { file, $"{file}.aria2", $"{file}.download" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException exception)
            {
                _logger.LogDebugMessage(
                    $"Delete invalid media file failed: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogDebugMessage(
                    $"Delete invalid media file was denied: {exception.Message}");
            }
        }
    }

    private static void NormalizeTransferSchemes(List<string> urls, bool useSsl)
    {
        for (var index = 0; index < urls.Count; index++)
        {
            var url = urls[index];
            if (useSsl && url.StartsWith("http://", StringComparison.Ordinal))
            {
                urls[index] = "https://" + url["http://".Length..];
            }
            else if (!useSsl && url.StartsWith("https://", StringComparison.Ordinal))
            {
                urls[index] = "http://" + url["https://".Length..];
            }
        }
    }

    private sealed class PendingDurlDownload(PlayUrlDurl durl)
    {
        public PlayUrlDurl Durl { get; } = durl;

        public string? FilePath { get; set; }
    }
}
