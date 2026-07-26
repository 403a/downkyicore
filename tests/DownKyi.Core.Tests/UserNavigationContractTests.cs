using DownKyi.Core.BiliApi;
using DownKyi.Core.BiliApi.Sign;
using DownKyi.Core.BiliApi.Users;
using DownKyi.Core.BiliApi.Users.Models;

namespace DownKyi.Core.Tests;

public sealed class UserNavigationContractTests
{
    [Fact]
    public async Task AnonymousNavigationResponsePreservesPublicWbiMetadata()
    {
        var client = CreateAnonymousClient();

        var navigation = await client.GetUserInfoForNavigationAsync(
            TestContext.Current.CancellationToken);

        Assert.NotNull(navigation);
        Assert.False(navigation.IsLogin);
        Assert.Equal(0, navigation.Mid);
        Assert.Equal("11111111111111111111111111111111", WbiKeyProvider.ExtractKey(navigation.Wbi?.ImageAddress));
        Assert.Equal("22222222222222222222222222222222", WbiKeyProvider.ExtractKey(navigation.Wbi?.SubAddress));
    }

    [Fact]
    public async Task AnonymousCodeRemainsRejectedOutsideTheNavigationContract()
    {
        var client = CreateAnonymousClient();

        var exception = await Assert.ThrowsAsync<BilibiliApiResponseException>(() =>
            BiliApiRequest.RequestJsonAsync<UserInfoForNavigationOrigin>(
                client,
                "https://example.test/not-nav",
                referer: null,
                operationName: "ordinary-contract",
                logTag: nameof(UserNavigationContractTests),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(-101, exception.Code);
    }

    private static StubBilibiliApiClient CreateAnonymousClient()
    {
        var body = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "DownKyi.Core.Tests",
            "BiliApi",
            "JsonSamples",
            "user-navigation-anonymous.json"));
        return new StubBilibiliApiClient((_, _) => Task.FromResult(body));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DownKyi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
