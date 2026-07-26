using DownKyi.Services.Account;
using Microsoft.Extensions.Logging.Abstractions;

namespace DownKyi.Tests;

public sealed class LoginCoordinatorTests
{
    [Fact]
    public async Task RequestLoginUrlPreservesCancellationBeforeNetworkWork()
    {
        var coordinator = new LoginCoordinator(
            NullLogger<LoginCoordinator>.Instance,
            new TestBilibiliApiClient());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.RequestLoginUrlAsync(cancellation.Token));
    }
}
