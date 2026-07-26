using DownKyi.Application.Desktop;
using DownKyi.Services;
using DownKyi.ViewModels.PageViewModels;
using ApiFavoritesMedia = DownKyi.Core.BiliApi.Favorites.Models.FavoritesMedia;

namespace DownKyi.Tests;

public sealed class FavoritesCoordinatorTests
{
    [Fact]
    public async Task PreCanceledFolderLoadDoesNotCallFavoritesApi()
    {
        var coordinator = new FavoritesCoordinator(new ThrowingFavoritesService());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.LoadFoldersAsync(42, cancellation.Token));
    }

    [Fact]
    public async Task PreCanceledPublicLoadDoesNotCallFavoritesApi()
    {
        var coordinator = new FavoritesCoordinator(new ThrowingFavoritesService());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.LoadPublicFavoritesAsync(42, cancellation.Token));
    }

    [Fact]
    public async Task SearchPassesKeywordAndPreservesHasMore()
    {
        using var settings = new TestSettingsStore();
        var service = new RecordingFavoritesService(settings);
        var coordinator = new FavoritesCoordinator(service);

        var page = await coordinator.LoadMediaPageAsync(42, 2, 20, "needle", CancellationToken.None);

        Assert.Equal((42L, 2, 20, "needle"), service.LastRequest);
        Assert.True(page.HasMore);
        Assert.Single(page.Medias);
    }

    private sealed class ThrowingFavoritesService : IFavoritesService
    {
        public Task<FavoritesPageItem?> GetFavoritesAsync(
            long mediaId,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The API should not be called for a canceled request.");
        }

        public Task<DownKyi.Core.BiliApi.Favorites.Models.FavoritesMediaResource> GetFavoritesMediaPageAsync(
            long mediaId,
            int page,
            int pageSize,
            string? keyword,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The API should not be called for a canceled request.");
        }

        public Task<IReadOnlyList<ApiFavoritesMedia>> GetAllFavoritesMediaAsync(
            long mediaId,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The API should not be called for a canceled request.");
        }

        public IReadOnlyList<FavoritesMedia> MapFavoritesMedia(
            IReadOnlyList<ApiFavoritesMedia> medias,
            AppRoute parentRoute,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Mapping should not run for a canceled request.");
        }

        public Task<IReadOnlyList<TabHeader>> GetCreatedFavoritesAsync(
            long mid,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The API should not be called for a canceled request.");
        }

        public Task<IReadOnlyList<TabHeader>> GetCollectedFavoritesAsync(
            long mid,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The API should not be called for a canceled request.");
        }
    }

    private sealed class RecordingFavoritesService : IFavoritesService
    {
        private readonly TestSettingsStore _settings;

        public RecordingFavoritesService(TestSettingsStore settings)
        {
            _settings = settings;
        }

        public (long MediaId, int Page, int PageSize, string? Keyword) LastRequest { get; private set; }

        public Task<FavoritesPageItem?> GetFavoritesAsync(
            long mediaId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FavoritesPageItem?>(null);

        public Task<DownKyi.Core.BiliApi.Favorites.Models.FavoritesMediaResource> GetFavoritesMediaPageAsync(
            long mediaId,
            int page,
            int pageSize,
            string? keyword,
            CancellationToken cancellationToken)
        {
            LastRequest = (mediaId, page, pageSize, keyword);
            return Task.FromResult(new DownKyi.Core.BiliApi.Favorites.Models.FavoritesMediaResource
            {
                HasMore = true,
                Medias = [new ApiFavoritesMedia { Id = 1, Bvid = "BV1fixture01", Title = "fixture" }]
            });
        }

        public Task<IReadOnlyList<ApiFavoritesMedia>> GetAllFavoritesMediaAsync(
            long mediaId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApiFavoritesMedia>>(Array.Empty<ApiFavoritesMedia>());

        public IReadOnlyList<FavoritesMedia> MapFavoritesMedia(
            IReadOnlyList<ApiFavoritesMedia> medias,
            AppRoute parentRoute,
            CancellationToken cancellationToken)
        {
            return [new FavoritesMedia(new TestNavigationService(), parentRoute, _settings.Store)
            {
                Bvid = medias[0].Bvid,
                Title = medias[0].Title
            }];
        }

        public Task<IReadOnlyList<TabHeader>> GetCreatedFavoritesAsync(
            long mid,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TabHeader>>([]);

        public Task<IReadOnlyList<TabHeader>> GetCollectedFavoritesAsync(
            long mid,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TabHeader>>([]);
    }
}
