using DownKyi.Application.Bilibili;

namespace DownKyi.Core.Tests;

internal sealed class StubBilibiliApiClient : IBilibiliApiClient
{
    private readonly Func<BilibiliHttpRequest, CancellationToken, Task<string>> _getStringAsync;
    private readonly Func<BilibiliHttpRequest, CancellationToken, Task<Stream>> _openReadAsync;

    public StubBilibiliApiClient(
        Func<BilibiliHttpRequest, CancellationToken, Task<string>> getStringAsync)
        : this(
            getStringAsync,
            static (_, _) => Task.FromException<Stream>(
                new NotSupportedException("This test client does not provide streams.")))
    {
    }

    public StubBilibiliApiClient(
        Func<BilibiliHttpRequest, CancellationToken, Task<string>> getStringAsync,
        Func<BilibiliHttpRequest, CancellationToken, Task<Stream>> openReadAsync)
    {
        _getStringAsync = getStringAsync
                          ?? throw new ArgumentNullException(nameof(getStringAsync));
        _openReadAsync = openReadAsync
                         ?? throw new ArgumentNullException(nameof(openReadAsync));
    }

    public Task<string> GetStringAsync(
        BilibiliHttpRequest request,
        CancellationToken cancellationToken)
    {
        return _getStringAsync(request, cancellationToken);
    }

    public Task<Stream> OpenReadAsync(
        BilibiliHttpRequest request,
        CancellationToken cancellationToken)
    {
        return _openReadAsync(request, cancellationToken);
    }

    public async Task DownloadFileAsync(
        BilibiliHttpRequest request,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var input = await OpenReadAsync(request, cancellationToken).ConfigureAwait(false);
        await using (input.ConfigureAwait(false))
        {
            var output = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous);
            await using (output.ConfigureAwait(false))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
