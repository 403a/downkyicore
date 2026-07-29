using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using DownKyi.Application.Bilibili;

namespace DownKyi.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class RequestPreparationBenchmarks
{
    private const string SampleJson = """
        {
          "code": 0,
          "message": "0",
          "data": {
            "bvid": "BV1xx411c7mD",
            "cid": 171776208,
            "title": "benchmark sample"
          }
        }
        """;

    private readonly JsonSerializerOptions _jsonOptions = new();
    private readonly string _requestAddress =
        "https://api.bilibili.com/x/player/wbi/playurl"
        + "?platform=html5&bvid=BV1xx411c7mD&cid=171776208&qn=120&fnval=4048&fourk=1";

    [Benchmark]
    public BilibiliHttpRequest BuildRequestContract()
    {
        return new BilibiliHttpRequest(
            _requestAddress,
            "https://www.bilibili.com/video/BV1xx411c7mD");
    }

    [Benchmark]
    public JsonElement DeserializeApiEnvelope()
    {
        return JsonSerializer.Deserialize<JsonElement>(SampleJson, _jsonOptions);
    }
}
