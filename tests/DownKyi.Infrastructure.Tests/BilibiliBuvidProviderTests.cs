using System.Net;
using DownKyi.Infrastructure.Bilibili;

namespace DownKyi.Infrastructure.Tests;

public sealed class BilibiliBuvidProviderTests
{
    [Fact]
    public async Task ConcurrentCallersShareOneFingerprintRequest()
    {
        var calls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = new TestHttpClientFactory(
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref calls);
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return BilibiliTestResponses.Json(
                    body: """{"code":0,"data":{"b_3":"synthetic-3","b_4":"synthetic-4"}}""");
            });
        var provider = CreateProvider(factory);

        var requests = Enumerable.Range(0, 10)
            .Select(_ => provider.GetAsync(TestContext.Current.CancellationToken))
            .ToArray();
        release.TrySetResult();
        var values = await Task.WhenAll(requests);

        Assert.Equal(1, calls);
        Assert.All(values, value =>
        {
            Assert.Equal("synthetic-3", value.Buvid3);
            Assert.Equal("synthetic-4", value.Buvid4);
        });
    }

    [Fact]
    public async Task CancelingOneWaiterDoesNotCancelSharedFingerprintRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = new TestHttpClientFactory(
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return BilibiliTestResponses.Json(
                    body: """{"code":0,"data":{"b_3":"synthetic-3","b_4":"synthetic-4"}}""");
            });
        var provider = CreateProvider(factory);
        using var canceledWaiter = new CancellationTokenSource();

        var first = provider.GetAsync(canceledWaiter.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = provider.GetAsync(TestContext.Current.CancellationToken);
        await canceledWaiter.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.TrySetResult();
        var value = await second.ConfigureAwait(true);

        Assert.Equal("synthetic-3", value.Buvid3);
        Assert.Equal("synthetic-4", value.Buvid4);
    }

    [Fact]
    public async Task InvalidFingerprintPayloadIsNotCached()
    {
        var calls = 0;
        using var factory = new TestHttpClientFactory(
            (_, _) =>
            {
                calls++;
                var body = calls == 1
                    ? """{"code":0,"data":{"b_3":"synthetic-3"}}"""
                    : """{"code":0,"data":{"b_3":"synthetic-3","b_4":"synthetic-4"}}""";
                return BilibiliTestResponses.CompletedJson(body: body);
            });
        var provider = CreateProvider(factory);

        await Assert.ThrowsAsync<Application.Bilibili.BilibiliHttpRequestException>(
            () => provider.GetAsync(TestContext.Current.CancellationToken));
        var value = await provider.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.Equal("synthetic-4", value.Buvid4);
    }

    private static BilibiliBuvidProvider CreateProvider(IHttpClientFactory factory)
    {
        var transport = new BilibiliHttpTransport(
            factory,
            TimeProvider.System,
            static (_, _) => Task.CompletedTask);
        return new BilibiliBuvidProvider(transport);
    }
}
