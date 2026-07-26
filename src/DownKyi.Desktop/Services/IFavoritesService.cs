using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Desktop;
using DownKyi.Presentation;
using ApiFavoritesMedia = DownKyi.Core.BiliApi.Favorites.Models.FavoritesMedia;
using ApiFavoritesMediaResource = DownKyi.Core.BiliApi.Favorites.Models.FavoritesMediaResource;

namespace DownKyi.Services;

internal interface IFavoritesService
{
    Task<FavoritesPageItem?> GetFavoritesAsync(
        long mediaId,
        CancellationToken cancellationToken = default);

    Task<ApiFavoritesMediaResource> GetFavoritesMediaPageAsync(
        long mediaId,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiFavoritesMedia>> GetAllFavoritesMediaAsync(
        long mediaId,
        CancellationToken cancellationToken);

    IReadOnlyList<FavoritesMedia> MapFavoritesMedia(
        IReadOnlyList<ApiFavoritesMedia> medias,
        AppRoute parentRoute,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TabHeader>> GetCreatedFavoritesAsync(
        long mid,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TabHeader>> GetCollectedFavoritesAsync(
        long mid,
        CancellationToken cancellationToken);
}
