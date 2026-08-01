using System.Net;
using DownKyi.Core.Aria2cNet.Client;
using DownKyi.Core.Aria2cNet.Client.Entity;
using DownKyi.TestInfrastructure;
using Newtonsoft.Json;

namespace DownKyi.Core.Tests;

public sealed class AriaClientSecurityTests
{
    [Theory]
    [InlineData("http://192.0.2.10")]
    [InlineData("http://aria.example")]
    public void RemotePlaintextRpcEndpointsAreRejected(string host)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new AriaClient(host, 6800, "test-token"));

        Assert.Contains("must use HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://[::1]")]
    [InlineData("https://aria.example")]
    public void LoopbackHttpAndRemoteHttpsRpcEndpointsAreAccepted(string host)
    {
        _ = new AriaClient(host, 6800, "test-token");
    }

    [Fact]
    public void RpcEndpointUserInformationIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new AriaClient("https://user:password@aria.example", 6800, "test-token"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RpcSecretMustNotBeEmpty(string token)
    {
        Assert.Throws<ArgumentException>(() =>
            new AriaClient("http://localhost", 6800, token));
    }

    [Fact]
    public async Task RemoteHttpsRpcRejectsAnUntrustedCertificate()
    {
        using var certificate = TestCertificateAuthority.CreateSelfSignedServerCertificate();
        var server = new LoopbackTlsFileServer(
            _ => certificate,
            "{}"u8.ToArray());
        await using var serverLifetime = server.ConfigureAwait(false);
        var client = new AriaClient(
            "https://localhost",
            server.Url.Port,
            "test-token");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetGlobalOptionAsync())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task RpcTransportDoesNotFollowRedirects()
    {
        var target = new LoopbackHttpServer(_ =>
            new LoopbackResponse(HttpStatusCode.OK, "{}"));
        await using var targetLifetime = target.ConfigureAwait(true);
        var redirect = new LoopbackHttpServer(_ =>
            new LoopbackResponse(
                HttpStatusCode.Redirect,
                Headers: new Dictionary<string, string>
                {
                    ["Location"] = target.Url.AbsoluteUri
                }));
        await using var redirectLifetime = redirect.ConfigureAwait(true);
        var client = new AriaClient(
            $"http://127.0.0.1",
            redirect.Url.Port,
            "test-token");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetGlobalOptionAsync())
            .ConfigureAwait(true);

        Assert.Equal(1, redirect.RequestCount);
        Assert.Equal(0, target.RequestCount);
    }

    [Fact]
    public void DownloadProxyUsesHttpsScopeWithoutEmbeddingCredentials()
    {
        var option = new AriaSendOption
        {
            HttpsProxy = "http://127.0.0.1:7890/"
        };

        var json = JsonConvert.SerializeObject(option);

        Assert.Contains("\"https-proxy\":\"http://127.0.0.1:7890/\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("all-proxy", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Proxy-Authorization", json, StringComparison.OrdinalIgnoreCase);
    }
}
