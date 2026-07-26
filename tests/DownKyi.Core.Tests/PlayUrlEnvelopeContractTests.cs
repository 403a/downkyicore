using DownKyi.Core.BiliApi;
using DownKyi.Core.BiliApi.Sign;
using DownKyi.Core.BiliApi.VideoStream;
using DownKyi.Core.BiliApi.VideoStream.Models;
using Newtonsoft.Json;

namespace DownKyi.Core.Tests;

public sealed class PlayUrlEnvelopeContractTests
{
    private const string ImgKey = "12345678901234567890123456789012";
    private const string SubKey = "abcdefghijklmnopqrstuvwxyzABCDEF";
    private static readonly WbiKeys Keys = new(ImgKey, SubKey);
    private static readonly string SampleDirectory = Path.Combine(
        FindRepositoryRoot(),
        "tests",
        "DownKyi.Core.Tests",
        "BiliApi",
        "JsonSamples");

    [Fact]
    public void MissingEnvelopeFieldsRemainNull()
    {
        var response = ReadSample("playurl-missing-payload.json");

        Assert.Null(response.Data);
        Assert.Null(response.Result);
    }

    [Fact]
    public void DataOnlyResponseSelectsData()
    {
        var response = ReadSample("playurl-video-data.json");

        var payload = VideoStreamApi.SelectPlayUrlPayload(
            response,
            VideoStreamApi.PlayUrlPayloadField.Data,
            "video");

        Assert.Same(response.Data, payload);
        Assert.Null(response.Result);
        Assert.Equal(80, Assert.Single(payload.Dash.Video).Id);
    }

    [Fact]
    public void ResultOnlyResponseSelectsResultWithoutEmptyDataMaskingIt()
    {
        var response = ReadSample("playurl-bangumi-result.json");

        var payload = VideoStreamApi.SelectPlayUrlPayload(
            response,
            VideoStreamApi.PlayUrlPayloadField.Result,
            "bangumi");

        Assert.Null(response.Data);
        Assert.Same(response.Result, payload);
        Assert.Equal(1, Assert.Single(payload.Durl).Order);
    }

    [Fact]
    public void MissingExpectedEnvelopeThrowsTypedContractFailure()
    {
        var response = ReadSample("playurl-missing-payload.json");

        var exception = Assert.Throws<BilibiliApiResponseException>(() =>
            VideoStreamApi.SelectPlayUrlPayload(
                response,
                VideoStreamApi.PlayUrlPayloadField.Data,
                "video"));

        Assert.Equal("video", exception.Operation);
        Assert.Contains("no 'data'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentButEmptyEnvelopeThrowsTypedContractFailure()
    {
        var response = ReadSample("playurl-empty-data.json");

        var exception = Assert.Throws<BilibiliApiResponseException>(() =>
            VideoStreamApi.SelectPlayUrlPayload(
                response,
                VideoStreamApi.PlayUrlPayloadField.Data,
                "video"));

        Assert.Contains("empty 'data'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrdinaryVideoEndpointUsesDataEnvelope()
    {
        var client = CreateClient("playurl-video-data.json");

        var payload = await client.GetVideoPlayUrlAsync(
            Keys,
            1702204169,
            1,
            "BV1fixture",
            2,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(80, Assert.Single(payload?.Dash.Video ?? []).Id);
    }

    [Fact]
    public async Task BangumiEndpointUsesResultVideoInfoEnvelope()
    {
        var client = CreateClient("playurl-bangumi-v2-result.json");

        var payload = await client.GetBangumiPlayUrlAsync(
            1,
            "BV1fixture",
            2,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(payload?.Durl ?? []).Order);
    }

    [Fact]
    public void BangumiV2MissingVideoInfoThrowsTypedContractFailure()
    {
        var response = new BangumiPlayUrlV2Origin
        {
            Result = new BangumiPlayUrlV2Result()
        };

        var exception = Assert.Throws<BilibiliApiResponseException>(() =>
            BangumiPlayUrlV2Contract.SelectPayload(response, "bangumi-v2"));

        Assert.Equal("bangumi-v2", exception.Operation);
        Assert.Contains("result.video_info", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheeseEndpointUsesDataEnvelope()
    {
        var client = CreateClient("playurl-cheese-data.json");

        var payload = await client.GetCheesePlayUrlAsync(
            1,
            "BV1fixture",
            2,
            3489,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(30280, Assert.Single(payload?.Dash.Audio ?? []).Id);
    }

    [Fact]
    public async Task OrdinaryVideoEndpointRejectsEmptyDataEnvelope()
    {
        var client = CreateClient("playurl-empty-data.json");

        var exception = await Assert.ThrowsAsync<BilibiliApiResponseException>(() =>
            client.GetVideoPlayUrlAsync(
                Keys,
                1702204169,
                1,
                "BV1fixture",
                2,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(nameof(VideoStreamApi.GetVideoPlayUrlAsync), exception.Operation);
    }

    private static PlayUrlOrigin ReadSample(string name)
    {
        return JsonConvert.DeserializeObject<PlayUrlOrigin>(
                   File.ReadAllText(Path.Combine(SampleDirectory, name)))
               ?? throw new InvalidDataException($"Sample '{name}' did not deserialize.");
    }

    private static StubBilibiliApiClient CreateClient(string sampleName)
    {
        var body = File.ReadAllText(Path.Combine(SampleDirectory, sampleName));
        return new StubBilibiliApiClient((_, _) => Task.FromResult(body));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "DownKyi.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
