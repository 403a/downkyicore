using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Desktop;
using DownKyi.Domain.Downloads;
using DownKyi.Models;
using DownKyi.ViewModels;
using DownKyi.ViewModels.DownloadManager;

namespace DownKyi.Services.Download;

internal enum DownloadArtifactOpenResult
{
    Opened,
    NotFound,
    OpenFailed
}

internal interface IDownloadManagerCoordinator
{
    Task PauseAllAsync(
        IEnumerable<DownloadingItem> items,
        CancellationToken cancellationToken = default);

    Task ResumeAllAsync(
        IEnumerable<DownloadingItem> items,
        CancellationToken cancellationToken = default);

    Task ToggleAsync(DownloadingItem item, CancellationToken cancellationToken = default);

    Task DeleteAsync(DownloadingItem item, CancellationToken cancellationToken = default);

    Task DeleteAllAsync(
        IEnumerable<DownloadingItem> items,
        CancellationToken cancellationToken = default);

    Task ClearDownloadedAsync(CancellationToken cancellationToken = default);

    Task RemoveDownloadedAsync(DownloadedItem item, CancellationToken cancellationToken = default);

    Task<DownloadArtifactOpenResult> OpenVideoAsync(
        DownloadedItem item,
        CancellationToken cancellationToken = default);

    Task<DownloadArtifactOpenResult> OpenFolderAsync(
        DownloadedItem item,
        CancellationToken cancellationToken = default);
}

internal sealed class DownloadManagerCoordinator : IDownloadManagerCoordinator
{
    private static readonly FrozenDictionary<string, ImmutableArray<string>> FileSuffixMap =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            ["downloadVideo"] = [".mp4", ".flv"],
            ["downloadAudio"] = [".aac", ".mp3"],
            ["downloadCover"] = [".jpg", ".jpeg", ".png", ".webp"],
            ["downloadDanmaku"] = [".ass"],
            ["downloadSubtitle"] = [".srt"]
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly DownloadTaskProjectionStore _storage;
    private readonly DownloadTaskStateWriter _stateWriter;
    private readonly DownloadTaskFileService _fileService;
    private readonly DownloadListState _downloadLists;
    private readonly IPlatformLauncher _platformLauncher;

    public DownloadManagerCoordinator(
        DownloadTaskProjectionStore storage,
        DownloadTaskStateWriter stateWriter,
        DownloadTaskFileService fileService,
        DownloadListState downloadLists,
        IPlatformLauncher platformLauncher)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _downloadLists = downloadLists ?? throw new ArgumentNullException(nameof(downloadLists));
        _platformLauncher = platformLauncher ?? throw new ArgumentNullException(nameof(platformLauncher));
    }

    public async Task PauseAllAsync(
        IEnumerable<DownloadingItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Downloading.DownloadStatus is DownloadStatus.NotStarted
                or DownloadStatus.WaitForDownload
                or DownloadStatus.Downloading)
            {
                await _stateWriter.PauseAsync(
                    GetTaskId(item),
                    cancellationToken).ConfigureAwait(true);
            }
        }
    }

    public async Task ResumeAllAsync(
        IEnumerable<DownloadingItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Downloading.DownloadStatus is DownloadStatus.NotStarted
                or DownloadStatus.WaitForDownload
                or DownloadStatus.PauseStarted
                or DownloadStatus.Pause
                or DownloadStatus.DownloadFailed)
            {
                await _stateWriter.ResumeAsync(
                    GetTaskId(item),
                    cancellationToken).ConfigureAwait(true);
            }
        }
    }

    public Task ToggleAsync(
        DownloadingItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Downloading.DownloadStatus switch
        {
            DownloadStatus.NotStarted or DownloadStatus.WaitForDownload =>
                _stateWriter.PauseAsync(GetTaskId(item), cancellationToken),
            DownloadStatus.PauseStarted or DownloadStatus.Pause or DownloadStatus.DownloadFailed =>
                _stateWriter.ResumeAsync(GetTaskId(item), cancellationToken),
            DownloadStatus.Downloading => _stateWriter.PauseAsync(GetTaskId(item), cancellationToken),
            _ => Task.FromResult(_storage.GetRequiredSnapshot(GetTaskId(item)))
        };
    }

    public async Task DeleteAsync(
        DownloadingItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        var taskId = GetTaskId(item);
        if (_storage.GetRequiredSnapshot(taskId).Phase != DownloadPhase.Canceled)
        {
            await _stateWriter.CancelAsync(taskId, cancellationToken).ConfigureAwait(true);
        }

        await _fileService.CancelActiveDownloadAsync(item).ConfigureAwait(true);

        // Once physical deletion starts, finish the database/list transaction even if app shutdown is requested.
        var deletion = await _fileService
            .DeleteGeneratedFilesAsync(item, CancellationToken.None)
            .ConfigureAwait(true);
        if (!deletion.Succeeded)
        {
            throw new IOException("One or more generated download files could not be deleted.");
        }

        await _stateWriter.DeleteAsync(taskId, CancellationToken.None).ConfigureAwait(true);
        _downloadLists.Downloading.Remove(item);
    }

    public async Task DeleteAllAsync(
        IEnumerable<DownloadingItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeleteAsync(item, cancellationToken).ConfigureAwait(true);
        }
    }

    public async Task ClearDownloadedAsync(CancellationToken cancellationToken = default)
    {
        await _storage.ClearDownloadedAsync(cancellationToken).ConfigureAwait(true);
        _downloadLists.Downloaded.Clear();
    }

    public async Task RemoveDownloadedAsync(
        DownloadedItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _storage.RemoveDownloadedAsync(item, cancellationToken).ConfigureAwait(true);
        _downloadLists.Downloaded.Remove(item);
    }

    public Task<DownloadArtifactOpenResult> OpenVideoAsync(
        DownloadedItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return OpenFirstFileAsync(item.DownloadBase?.FilePath, [".mp4", ".flv"], cancellationToken);
    }

    public async Task<DownloadArtifactOpenResult> OpenFolderAsync(
        DownloadedItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        var downloadBase = item.DownloadBase;
        if (downloadBase == null || string.IsNullOrWhiteSpace(downloadBase.FilePath))
        {
            return DownloadArtifactOpenResult.NotFound;
        }

        foreach (var suffix in GetSelectedSuffixes(downloadBase))
        {
            var candidate = downloadBase.FilePath + suffix;
            if (!File.Exists(candidate))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(candidate));
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            return await _platformLauncher.OpenFolderAsync(directory, cancellationToken)
                .ConfigureAwait(true)
                ? DownloadArtifactOpenResult.Opened
                : DownloadArtifactOpenResult.OpenFailed;
        }

        return DownloadArtifactOpenResult.NotFound;
    }

    private async Task<DownloadArtifactOpenResult> OpenFirstFileAsync(
        string? basePath,
        IEnumerable<string> suffixes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return DownloadArtifactOpenResult.NotFound;
        }

        foreach (var suffix in suffixes)
        {
            var candidate = basePath + suffix;
            if (!File.Exists(candidate))
            {
                continue;
            }

            return await _platformLauncher.OpenFileAsync(Path.GetFullPath(candidate), cancellationToken)
                .ConfigureAwait(true)
                ? DownloadArtifactOpenResult.Opened
                : DownloadArtifactOpenResult.OpenFailed;
        }

        return DownloadArtifactOpenResult.NotFound;
    }

    private static IEnumerable<string> GetSelectedSuffixes(DownloadBase downloadBase)
    {
        return downloadBase.NeedDownloadContent
            .Where(item => item.Value && FileSuffixMap.ContainsKey(item.Key))
            .SelectMany(item => FileSuffixMap[item.Key]);
    }

    private static DownloadTaskId GetTaskId(DownloadingItem item)
    {
        return new DownloadTaskId(item.DownloadBase.Id);
    }
}
