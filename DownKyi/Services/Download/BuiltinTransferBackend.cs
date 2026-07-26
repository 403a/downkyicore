using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Core.BiliApi.Login;
using DownKyi.Core.Logging;
using DownKyi.Core.Settings;
using DownKyi.Core.Utils;
using DownKyi.Domain.Downloads;
using DownKyi.Utils;
using Downloader;
using Microsoft.Extensions.Logging;

namespace DownKyi.Services.Download;

internal sealed class BuiltinTransferBackend : ITransferBackend
{
    private readonly ISettingsStore _settingsStore;
    private readonly DownloadDiagnosticLogger _diagnosticLogger;
    private readonly ILogger<BuiltinTransferBackend> _logger;

    public BuiltinTransferBackend(
        ISettingsStore settingsStore,
        DownloadDiagnosticLogger diagnosticLogger,
        ILogger<BuiltinTransferBackend> logger)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _diagnosticLogger = diagnosticLogger ?? throw new ArgumentNullException(nameof(diagnosticLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "built-in";

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task<DownloadTransferOutcome> TransferAsync(DownloadTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var urls = request.Urls;
        var path = request.Directory;
        var localFileName = request.FileName;
        var expectedBytes = request.ExpectedBytes;
        var network = _settingsStore.Current.Network;
        var requestConfiguration = new RequestConfiguration
        {
            Headers = new WebHeaderCollection
            {
                { "cookie", LoginHelper.GetLoginInfoCookiesString() }
            },
            UserAgent = network.UserAgent,
            Referer = "https://www.bilibili.com"
        };
        if (network.IsHttpProxy == AllowStatus.Yes)
        {
            requestConfiguration.Proxy = new WebProxy(
                network.HttpProxy,
                network.HttpProxyListenPort);
        }

        var split = network.Split;
        var configuration = new DownloadConfiguration
        {
            ChunkCount = split,
            RequestConfiguration = requestConfiguration,
            ParallelDownload = true,
            ParallelCount = split,
            MaximumMemoryBufferBytes = 50 * 1024 * 1024,
            EnableAutoResumeDownload = true,
            ClearPackageOnCompletionWithFailure = false,
            FileExistPolicy = FileExistPolicy.IgnoreDownload
        };

        foreach (var url in urls)
        {
            var targetFile = Path.Combine(path, localFileName);
            var totalBytesToReceive = expectedBytes;
            var receivedBytes = 0L;
            var progressUpdater = new DownloadProgressUiUpdater(
                TimeProvider.System,
                DownloadProgressUiUpdater.DefaultMinimumInterval);
            DownloadProgress? lastProgress = null;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _diagnosticLogger.LogBuiltInTaskStart(
                Name,
                localFileName,
                urls.Count,
                configuration.ChunkCount,
                configuration.ParallelCount,
                network);

            using var downloader = new Downloader.DownloadService(configuration);
            downloader.DownloadStarted += (_, args) =>
            {
                if (args.TotalBytesToReceive > 0)
                {
                    totalBytesToReceive = (long)args.TotalBytesToReceive;
                }
            };
            downloader.DownloadProgressChanged += (_, args) =>
            {
                receivedBytes = (long)Math.Max(0, args.ReceivedBytesSize);
                if (args.TotalBytesToReceive > 0)
                {
                    totalBytesToReceive = (long)args.TotalBytesToReceive;
                }

                var speed = (long)args.BytesPerSecondSpeed;
                if (progressUpdater.TryCreate(
                        args.ProgressPercentage,
                        args.ReceivedBytesSize,
                        args.TotalBytesToReceive,
                        speed,
                        out var progress))
                {
                    lastProgress = progress;
                    request.PublishProgress(progress);
                    _diagnosticLogger.LogSpeed(
                        Name,
                        localFileName,
                        args.ReceivedBytesSize,
                        args.TotalBytesToReceive,
                        speed);
                }
            };
            downloader.DownloadFileCompleted += (_, args) =>
            {
                if (args.Error != null)
                {
                    _logger.LogErrorMessage("Built-in download completion reported an error.", args.Error);
                }

                var succeeded = !args.Cancelled &&
                                args.Error == null &&
                                IsDownloadedMediaFileUsable(
                                    targetFile,
                                    expectedBytes,
                                    receivedBytes,
                                    totalBytesToReceive);
                request.SetBuiltinDownloadService(null);
                completion.TrySetResult(succeeded);
            };

            request.SetBuiltinDownloadService(downloader);
            var transferTask = downloader.DownloadFileTaskAsync(
                url,
                targetFile,
                request.CancellationToken);
            var taskSucceeded = false;
            try
            {
                while (!completion.Task.IsCompleted && !transferTask.IsCompleted)
                {
                    if (request.IsPauseRequested())
                    {
                        downloader.Pause();
                        downloader.CancelAsync();
                        request.SetBuiltinDownloadService(null);
                        if (lastProgress != null)
                        {
                            await request.PersistProgressAsync(lastProgress, CancellationToken.None)
                                .ConfigureAwait(true);
                        }

                        throw new OperationCanceledException("Download was paused.");
                    }

                    request.EnsureActive();

                    await Task.Delay(
                        TimeSpan.FromMilliseconds(100),
                        request.CancellationToken).ConfigureAwait(true);
                }

                await transferTask.ConfigureAwait(true);
                taskSucceeded = true;
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
                downloader.CancelAsync();
                request.SetBuiltinDownloadService(null);
                try
                {
                    await transferTask.ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    taskSucceeded = false;
                }

                throw;
            }
            catch (OperationCanceledException)
            {
                downloader.CancelAsync();
                try
                {
                    await transferTask.ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    taskSucceeded = false;
                }

                taskSucceeded = false;
            }
            catch (Exception e) when (e is IOException or HttpRequestException or InvalidOperationException)
            {
                _logger.LogErrorMessage("Built-in transfer failed.", e);
                taskSucceeded = false;
            }
            finally
            {
                request.SetBuiltinDownloadService(null);
            }

            completion.TrySetResult(taskSucceeded && IsDownloadedMediaFileUsable(
                targetFile,
                expectedBytes,
                receivedBytes,
                totalBytesToReceive));
            if (lastProgress != null)
            {
                await request.PersistProgressAsync(lastProgress, CancellationToken.None)
                    .ConfigureAwait(true);
            }

            if (await completion.Task.ConfigureAwait(true))
            {
                return DownloadTransferOutcome.Succeeded;
            }

            if (request.IsPauseRequested())
            {
                return DownloadTransferOutcome.Paused;
            }

            DeleteInvalidDownloadedMediaFile(targetFile);
            _logger.LogInformationMessage("Built-in transfer was incomplete; trying a backup endpoint.");
        }

        return DownloadTransferOutcome.Failed;
    }

    public void Dispose()
    {
    }

    private bool IsDownloadedMediaFileUsable(
        string? file,
        long expectedBytes = 0,
        long receivedBytes = 0,
        long totalBytesToReceive = 0)
    {
        var result = DownloadFileIntegrity.Check(file, expectedBytes, receivedBytes, totalBytesToReceive);
        if (!result.IsUsable)
        {
            _logger.LogInformationMessage(result.Reason ?? "Downloaded media file is not usable.");
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
            catch (IOException e)
            {
                _logger.LogDebugMessage($"Delete invalid media file failed: {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                _logger.LogDebugMessage($"Delete invalid media file was denied: {e.Message}");
            }
        }
    }
}
