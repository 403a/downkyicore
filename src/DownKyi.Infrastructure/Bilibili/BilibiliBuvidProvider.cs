using System.Text.Json;
using System.Text.Json.Serialization;
using DownKyi.Application.Bilibili;

namespace DownKyi.Infrastructure.Bilibili;

internal sealed class BilibiliBuvidProvider : IBuvidProvider
{
    private const string FingerprintEndpoint =
        "https://api.bilibili.com/x/frontend/finger/spi";
    private readonly BilibiliHttpTransport _transport;
    private readonly object _sync = new();
    private Task<BilibiliBuvid>? _loadTask;

    public BilibiliBuvidProvider(BilibiliHttpTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<BilibiliBuvid> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<BilibiliBuvid> loadTask;
        lock (_sync)
        {
            if (_loadTask is null || _loadTask.IsFaulted || _loadTask.IsCanceled)
            {
                _loadTask = LoadAsync();
            }

            loadTask = _loadTask;
        }

        return loadTask.WaitAsync(cancellationToken);
    }

    private async Task<BilibiliBuvid> LoadAsync()
    {
        var response = await _transport.GetStringAsync(
            static () => new HttpRequestMessage(HttpMethod.Get, FingerprintEndpoint),
            attempts: 2,
            CancellationToken.None).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize(
            response,
            BilibiliInfrastructureJsonContext.Default.BuvidEnvelope);
        if (envelope?.Code is { } code and not 0)
        {
            throw new BilibiliHttpRequestException(
                $"Bilibili fingerprint request failed with API code {code}.",
                BilibiliHttpFailureKind.HttpStatus);
        }

        if (string.IsNullOrWhiteSpace(envelope?.Data?.Buvid3)
            || string.IsNullOrWhiteSpace(envelope.Data.Buvid4))
        {
            throw new BilibiliHttpRequestException(
                "Bilibili fingerprint response did not contain both buvid values.",
                BilibiliHttpFailureKind.EmptyResponse);
        }

        return new BilibiliBuvid(envelope.Data.Buvid3, envelope.Data.Buvid4);
    }

    internal sealed class BuvidEnvelope
    {
        [JsonPropertyName("code")]
        public int Code { get; init; }

        [JsonPropertyName("data")]
        public BuvidPayload? Data { get; init; }
    }

    internal sealed class BuvidPayload
    {
        [JsonPropertyName("b_3")]
        public string? Buvid3 { get; init; }

        [JsonPropertyName("b_4")]
        public string? Buvid4 { get; init; }
    }
}

[JsonSerializable(typeof(BilibiliBuvidProvider.BuvidEnvelope))]
internal sealed partial class BilibiliInfrastructureJsonContext : JsonSerializerContext;
