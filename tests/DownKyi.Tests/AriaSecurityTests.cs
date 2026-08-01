using System.Net;
using System.Net.Http;
using DownKyi.Core.Settings;
using DownKyi.Services.Download;

namespace DownKyi.Tests;

public sealed class AriaSecurityTests
{
    [Fact]
    public void LocalEndpointsUseFreshHighEntropySecretsAndEphemeralPorts()
    {
        var first = LocalAriaRpcEndpoint.Create();
        var second = LocalAriaRpcEndpoint.Create();

        Assert.InRange(first.Port, 1024, 65535);
        Assert.InRange(second.Port, 1024, 65535);
        Assert.Equal(64, first.Secret.Length);
        Assert.Equal(64, second.Secret.Length);
        Assert.NotEqual(first.Secret, second.Secret);
        Assert.Matches("^[0-9A-F]{64}$", first.Secret);
        Assert.Matches("^[0-9A-F]{64}$", second.Secret);
    }

    [Fact]
    public void CredentialsAreScopedToHttpsBilibiliTasks()
    {
        var result = AriaTaskHeaderPolicy.Create(
            "https://api.bilibili.com/media",
            "DownKyi-Test-Agent",
            "test-cookie=value");

        Assert.True(result.CarriesCredentials);
        Assert.Contains("Cookie: test-cookie=value", result.Headers);
        Assert.Contains("Origin: https://www.bilibili.com", result.Headers);
        Assert.Contains("Referer: https://www.bilibili.com", result.Headers);
    }

    [Theory]
    [InlineData("http://api.bilibili.com/media")]
    [InlineData("https://bilibili.com.attacker.example/media")]
    [InlineData("https://bilivideo.com/media")]
    [InlineData("https://example.com/media")]
    public void CredentialsAreNotSentOutsideTheExactHttpsBilibiliScope(string address)
    {
        var result = AriaTaskHeaderPolicy.Create(
            address,
            "DownKyi-Test-Agent",
            "test-cookie=value");

        Assert.False(result.CarriesCredentials);
        Assert.DoesNotContain(
            result.Headers,
            header => header.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("unsafe\r\nX-Injected: value", "safe-cookie=value")]
    [InlineData("DownKyi-Test-Agent", "unsafe\r\nX-Injected: value")]
    [InlineData("unsafe\0agent", "safe-cookie=value")]
    public void TaskHeadersRejectControlCharacterInjection(
        string userAgent,
        string credentials)
    {
        Assert.Throws<ArgumentException>(() => AriaTaskHeaderPolicy.Create(
            "https://www.bilibili.com/media",
            userAgent,
            credentials));
    }

    [Fact]
    public void TlsReportSchemaExposesOnlySanitizedEvidenceFields()
    {
        var actual = typeof(Aria2TlsReport)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = new[]
        {
            nameof(Aria2TlsReport.Architecture),
            nameof(Aria2TlsReport.AriaVersion),
            nameof(Aria2TlsReport.AssetRuntimeIdentifier),
            nameof(Aria2TlsReport.BinarySha256),
            nameof(Aria2TlsReport.Cases),
            nameof(Aria2TlsReport.CertificateAuthoritySource),
            nameof(Aria2TlsReport.CommitSha),
            nameof(Aria2TlsReport.Complete),
            nameof(Aria2TlsReport.OperatingSystem),
            nameof(Aria2TlsReport.Passed),
            nameof(Aria2TlsReport.Runtime),
            nameof(Aria2TlsReport.RuntimeIdentifier),
            nameof(Aria2TlsReport.RequiredFeature),
            nameof(Aria2TlsReport.SchemaVersion),
            nameof(Aria2TlsReport.TlsBackend)
        }.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("127.0.0.1", "http://127.0.0.1:7890/")]
    [InlineData("localhost", "http://localhost:7890/")]
    [InlineData("::1", "http://[::1]:7890/")]
    [InlineData("[::1]", "http://[::1]:7890/")]
    public void HttpsDownloadProxyAcceptsOnlyExplicitLoopbackHosts(
        string host,
        string expected)
    {
        Assert.True(AriaHttpsProxyPolicy.TryCreateConnectProxyUri(
            host,
            7890,
            out var proxyUri));
        Assert.Equal(expected, proxyUri.AbsoluteUri);
        Assert.Equal(Uri.UriSchemeHttp, proxyUri.Scheme);
    }

    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData("proxy.example")]
    [InlineData("http://127.0.0.1")]
    [InlineData("user:password@127.0.0.1")]
    [InlineData("127.0.0.2")]
    public void HttpsDownloadProxyRejectsRemoteOrCredentialBearingHosts(string host)
    {
        Assert.False(AriaHttpsProxyPolicy.TryCreateConnectProxyUri(
            host,
            7890,
            out _));
    }

    [Fact]
    public async Task AddressResolverRejectsHttpsToHttpRedirectBeforeAriaReceivesIt()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(
            HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("http://download.example/media") }
        });
        using var resolver = AriaDownloadAddressResolver.CreateForTest(handler);

        var result = await resolver.ResolveAsync(
            "https://download.example/media?signature=private",
            "DownKyi-Test-Agent",
            credentials: null,
            TestContext.Current.CancellationToken);

        Assert.Equal("download.transfer.insecure-redirect", result.ErrorCode);
        Assert.Null(result.Address);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task AddressResolverRejectsEveryCredentialBearingRedirect()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(
            HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("https://api.bilibili.com/final") }
        });
        using var resolver = AriaDownloadAddressResolver.CreateForTest(handler);

        var result = await resolver.ResolveAsync(
            "https://api.bilibili.com/media",
            "DownKyi-Test-Agent",
            "test-cookie=value",
            TestContext.Current.CancellationToken);

        Assert.Equal("download.transfer.credentialed-redirect", result.ErrorCode);
        Assert.Null(result.Address);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task AddressResolverFollowsOnlyBoundedHttpsRedirects()
    {
        using var handler = new StubHttpMessageHandler((request, requestNumber) =>
        {
            if (requestNumber == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
                {
                    Headers = { Location = new Uri("/final", UriKind.Relative) }
                };
            }

            Assert.Equal("https://download.example/final", request.RequestUri?.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.PartialContent);
        });
        using var resolver = AriaDownloadAddressResolver.CreateForTest(handler);

        var result = await resolver.ResolveAsync(
            "https://download.example/media",
            "DownKyi-Test-Agent",
            credentials: null,
            TestContext.Current.CancellationToken);

        var acceptedAddress = Assert.IsType<Uri>(result.Address);
        var acceptedHeaders = Assert.IsType<AriaTaskHeaders>(result.Headers);
        Assert.Equal("https://download.example/final", acceptedAddress.AbsoluteUri);
        Assert.False(acceptedHeaders.CarriesCredentials);
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestNumber = Interlocked.Increment(ref _requestCount);
            return Task.FromResult(responseFactory(request, requestNumber));
        }
    }
}
