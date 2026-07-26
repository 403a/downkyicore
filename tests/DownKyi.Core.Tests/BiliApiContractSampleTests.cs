using DownKyi.Application.Bilibili;
using DownKyi.Core.BiliApi;
using Newtonsoft.Json.Linq;

namespace DownKyi.Core.Tests;

public sealed class BiliApiContractSampleTests
{
    private static readonly string SampleDirectory = Path.Combine(
        FindRepositoryRoot(),
        "tests",
        "DownKyi.Core.Tests",
        "BiliApi",
        "JsonSamples");

    [Fact]
    public async Task SuccessSampleDeserializes()
    {
        var result = await RequestSampleAsync("success.json");

        Assert.Equal(0, result.Value<int>("code"));
        Assert.Equal(1, result["data"]?.Value<int>("id"));
    }

    [Fact]
    public async Task MissingDataSampleIsVisibleWithoutNullReference()
    {
        var result = await RequestSampleAsync("missing-data.json");

        Assert.Equal(0, result.Value<int>("code"));
        Assert.Null(result["data"]);
    }

    [Fact]
    public async Task RejectedCodeThrowsTypedApiFailure()
    {
        var exception = await Assert.ThrowsAsync<BilibiliApiResponseException>(
            () => RequestSampleAsync("rejected.json"));

        Assert.Equal("sample", exception.Operation);
        Assert.Contains("code=-101", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("error.html")]
    [InlineData("malformed.json")]
    public async Task NonJsonSamplesThrowTypedApiFailure(string sampleName)
    {
        await Assert.ThrowsAsync<BilibiliApiResponseException>(
            () => RequestSampleAsync(sampleName));
    }

    private static Task<JObject> RequestSampleAsync(string sampleName)
    {
        var bodyTask = File.ReadAllTextAsync(
            Path.Combine(SampleDirectory, sampleName),
            TestContext.Current.CancellationToken);
        IBilibiliApiClient client = new StubBilibiliApiClient(
            async (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await bodyTask.ConfigureAwait(false);
            });
        return BiliApiRequest.RequestJsonAsync<JObject>(
            client,
            "https://example.com/getLogin",
            referer: null,
            operationName: "sample",
            logTag: nameof(BiliApiContractSampleTests),
            includeCredentials: false,
            cancellationToken: TestContext.Current.CancellationToken);
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
