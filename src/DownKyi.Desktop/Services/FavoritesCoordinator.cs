using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Desktop;
using DownKyi.Presentation;

namespace DownKyi.Services;

internal sealed record PublicFavoritesSnapshot(
    FavoritesPageItem Favorites,
    IReadOnlyList<FavoritesMedia> Medias);

internal sealed record FavoritesMediaPageSnapshot(
    IReadOnlyList<FavoritesMedia> Medias,
    bool HasMore);

internal interface IFavoritesCoordinator
{
    Task<IReadOnlyList<TabHeader>> LoadFoldersAsync(long mid, CancellationToken cancellationToken);

    Task<FavoritesMediaPageSnapshot> LoadMediaPageAsync(
        long favoritesId,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken);

    Task<PublicFavoritesSnapshot?> LoadPublicFavoritesAsync(
        long favoritesId,
        CancellationToken cancellationToken);
}

internal sealed class FavoritesCoordinator : IFavoritesCoordinator
{
    private readonly IFavoritesService _favoritesService;

    public FavoritesCoordinator(IFavoritesService favoritesService)
    {
        _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
    }

    public async Task<IReadOnlyList<TabHeader>> LoadFoldersAsync(
        long mid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var created = await _favoritesService.GetCreatedFavoritesAsync(mid, cancellationToken)
            .ConfigureAwait(false);
        var collected = await _favoritesService.GetCollectedFavoritesAsync(mid, cancellationToken)
            .ConfigureAwait(false);
        var result = new List<TabHeader>(created.Count + collected.Count);
        result.AddRange(created);
        result.AddRange(collected);
        return result;
    }

    public async Task<FavoritesMediaPageSnapshot> LoadMediaPageAsync(
        long favoritesId,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = await _favoritesService.GetFavoritesMediaPageAsync(
            favoritesId,
            page,
            pageSize,
            keyword,
            cancellationToken).ConfigureAwait(false);
        var mapped = resource.Medias.Count == 0
            ? Array.Empty<FavoritesMedia>()
            : _favoritesService.MapFavoritesMedia(
                resource.Medias,
                AppRoute.MyFavorites,
                cancellationToken);
        return new FavoritesMediaPageSnapshot(mapped, resource.HasMore);
    }

    public async Task<PublicFavoritesSnapshot?> LoadPublicFavoritesAsync(
        long favoritesId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var favorites = await _favoritesService.GetFavoritesAsync(
            favoritesId,
            cancellationToken).ConfigureAwait(false);
        if (favorites == null)
        {
            return null;
        }

        var medias = await _favoritesService.GetAllFavoritesMediaAsync(
            favoritesId,
            cancellationToken).ConfigureAwait(false);
        var mapped = _favoritesService.MapFavoritesMedia(
            medias,
            AppRoute.PublicFavorites,
            cancellationToken);
        return new PublicFavoritesSnapshot(favorites, mapped);
    }
}
