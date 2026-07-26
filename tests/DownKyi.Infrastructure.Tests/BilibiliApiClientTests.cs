using System.Net;
using DownKyi.Application.Bilibili;
using DownKyi.Infrastructure.Bilibili;
using DownKyi.TestInfrastructure;

namespace DownKyi.Infrastructure.Tests;

public sealed class BilibiliApiClientTests
{
    [Fact]
    public async Task AnonymousRequestDoesNotReadCredentialsOrBuvid()
    {
        using var factory = new TestHttpClientFactory(
            (_, _) => BilibiliTestResponses.CompletedJson());
        var buvid = new StubBuvidProvider();
        var client = CreateClient(factory, new ThrowingCookieProvider(), buvid);
        var request = new BilibiliHttpRequest(
            "https://example.invalid/api",
            includeCredentials: false);

        var result = await client.GetStringAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal("{}", result);
        Assert.Equal(0, buvid.Calls);
    }

    [Fact]
    public async Task DownloadRemovesPartialTemporaryFileWhenContentIsTruncated()
    {
        var server = new LoopbackHttpServer(_ =>
            new LoopbackResponse(
                HttpStatusCode.OK,
                "partial",
                ContentLength: 128));
        await using var serverLifetime = server.ConfigureAwait(false);
        using var factory = TestHttpClientFactory.CreateSockets(useProxy: false);
        var client = CreateClient(factory, new EmptyCookieProvider(), new StubBuvidProvider());
        var output = Path.Combine(
            Path.GetTempPath(),
            $"downkyi-partial-{Guid.NewGuid():N}.bin");

        try
        {
            await Assert.ThrowsAnyAsync<IOException>(() => client.DownloadFileAsync(
                new BilibiliHttpRequest(
                    server.Url.ToString(),
                    includeCredentials: false,
                    attempts: 1),
                output,
                TestContext.Current.CancellationToken));

            Assert.False(File.Exists(output));
            Assert.False(File.Exists($"{output}.download"));
        }
        finally
        {
            File.Delete(output);
            File.Delete($"{output}.download");
        }
    }

    private static BilibiliApiClient CreateClient(
        IHttpClientFactory factory,
        IBilibiliCookieProvider cookieProvider,
        IBuvidProvider buvidProvider)
    {
        var transport = new BilibiliHttpTransport(
            factory,
            TimeProvider.System,
            static (_, _) => Task.CompletedTask);
        return new BilibiliApiClient(transport, cookieProvider, buvidProvider);
    }
}
