using DownKyi.Services.Friends;

namespace DownKyi.Tests;

public sealed class FriendRelationCoordinatorTests
{
    [Fact]
    public async Task PreCanceledRequestsDoNotStartRelationApiWork()
    {
        var coordinator = new FriendRelationCoordinator(new TestBilibiliApiClient());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.LoadFollowingOverviewAsync(42, true, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.LoadFollowingPageAsync(
                42,
                FollowingListKind.All,
                -1,
                1,
                20,
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.LoadFollowerPageAsync(42, 1, 20, cancellation.Token));
    }
}
