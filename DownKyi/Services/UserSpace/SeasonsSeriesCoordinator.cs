using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Bilibili;
using DownKyi.Core.BiliApi.Users;
using DownKyi.Core.BiliApi.Users.Models;
using DownKyi.Services.Media;

namespace DownKyi.Services.UserSpace;

internal enum SeasonsSeriesKind
{
    Season = 1,
    Series = 2
}

internal sealed record SeasonsSeriesDownloadItem(string Bvid, bool IsSelected);

internal interface ISeasonsSeriesCoordinator
{
    Task<IReadOnlyList<SpaceSeasonsSeriesArchives>> LoadPageAsync(
        long mid,
        long id,
        SeasonsSeriesKind kind,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<int?> AddToDownloadAsync(
        IReadOnlyList<SeasonsSeriesDownloadItem> items,
        bool onlySelected,
        CancellationToken cancellationToken);
}

internal sealed class SeasonsSeriesCoordinator : ISeasonsSeriesCoordinator
{
    private readonly IContentDownloadCoordinator _downloadCoordinator;
    private readonly IBilibiliApiClient _client;

    public SeasonsSeriesCoordinator(
        IContentDownloadCoordinator downloadCoordinator,
        IBilibiliApiClient client)
    {
        _downloadCoordinator = downloadCoordinator ?? throw new ArgumentNullException(nameof(downloadCoordinator));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<SpaceSeasonsSeriesArchives>> LoadPageAsync(
        long mid,
        long id,
        SeasonsSeriesKind kind,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (kind)
        {
            case SeasonsSeriesKind.Season:
                var season = await _client.GetSeasonsDetailAsync(
                    mid,
                    id,
                    page,
                    pageSize,
                    cancellationToken).ConfigureAwait(false);
                return season == null || season.Meta.Total == 0
                    ? Array.Empty<SpaceSeasonsSeriesArchives>()
                    : season.Archives;
            case SeasonsSeriesKind.Series:
                var meta = await _client.GetSeriesMetaAsync(id, cancellationToken)
                    .ConfigureAwait(false);
                var series = await _client.GetSeriesDetailAsync(
                    mid,
                    id,
                    page,
                    pageSize,
                    cancellationToken).ConfigureAwait(false);
                return series == null || meta?.Meta.Total == 0
                    ? Array.Empty<SpaceSeasonsSeriesArchives>()
                    : series.Archives;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    public Task<int?> AddToDownloadAsync(
        IReadOnlyList<SeasonsSeriesDownloadItem> items,
        bool onlySelected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        var downloadItems = new List<ContentDownloadItem>(items.Count);
        foreach (var item in items)
        {
            downloadItems.Add(new ContentDownloadItem(item.Bvid, DownloadInfoKind.Video, item.IsSelected));
        }

        return _downloadCoordinator.AddAsync(
            downloadItems,
            onlySelected,
            cancellationToken);
    }

}
