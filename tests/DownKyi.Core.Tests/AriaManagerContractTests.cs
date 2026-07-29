using DownKyi.Core.Aria2cNet;
using DownKyi.Core.Aria2cNet.Client;
using Microsoft.Extensions.Logging.Abstractions;

namespace DownKyi.Core.Tests;

public sealed class AriaManagerContractTests
{
    [Fact]
    public async Task RpcRejectionFailsImmediatelyWithMachineReadableCode()
    {
        const string response =
            """
            {
              "jsonrpc": "2.0",
              "id": "test",
              "error": {
                "code": 1,
                "message": "Sanitized RPC rejection."
              }
            }
            """;
        var requestCount = 0;
        var manager = CreateManager(response, () => requestCount++);

        var result = await manager.GetDownloadStatusDetailAsync(
            "test-gid",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DownloadResult.FAILED, result.Result);
        Assert.Equal("rpc-1", result.ErrorCode);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task MissingGidRetainsAbortSemantics()
    {
        const string response =
            """
            {
              "jsonrpc": "2.0",
              "id": "test",
              "error": {
                "code": 1,
                "message": "GID test-gid is not found"
              }
            }
            """;
        var manager = CreateManager(response, static () => { });

        var result = await manager.GetDownloadStatusDetailAsync(
            "test-gid",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DownloadResult.ABORT, result.Result);
        Assert.Equal("not-found", result.ErrorCode);
    }

    [Fact]
    public async Task EmptyRpcEnvelopeFailsImmediately()
    {
        const string response =
            """
            {
              "jsonrpc": "2.0",
              "id": "test"
            }
            """;
        var requestCount = 0;
        var manager = CreateManager(response, () => requestCount++);

        var result = await manager.GetDownloadStatusDetailAsync(
            "test-gid",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DownloadResult.FAILED, result.Result);
        Assert.Equal("rpc-empty", result.ErrorCode);
        Assert.Equal(1, requestCount);
    }

    private static AriaManager CreateManager(
        string response,
        Action onRequest)
    {
        var client = new AriaClient(
            "http://localhost",
            35076,
            "test-token",
            (_, _) =>
            {
                onRequest();
                return Task.FromResult<string?>(response);
            });
        return new AriaManager(
            client,
            NullLogger<AriaManager>.Instance);
    }
}
