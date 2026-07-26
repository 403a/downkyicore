using DownKyi.Application.Bilibili;

namespace DownKyi.Tests;

internal sealed class TestBilibiliApiClient : IBilibiliApiClient
{
    public Func<BilibiliHttpRequest, CancellationToken, Task<string>>? GetStringAsyncHandler
    {
        get;
        init;
    }

    public Func<BilibiliHttpRequest, CancellationToken, Task<Stream>>? OpenReadAsyncHandler
    {
        get;
        init;
    }

    public Func<BilibiliHttpRequest, string, CancellationToken, Task>? DownloadFileAsyncHandler
    {
        get;
        init;
    }

    public Task<string> GetStringAsync(
        BilibiliHttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return GetStringAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromException<string>(
                   new InvalidOperationException("Unexpected Bilibili string request."));
    }

    public Task<Stream> OpenReadAsync(
        BilibiliHttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return OpenReadAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromException<Stream>(
                   new InvalidOperationException("Unexpected Bilibili stream request."));
    }

    public Task DownloadFileAsync(
        BilibiliHttpRequest request,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        return DownloadFileAsyncHandler?.Invoke(request, destinationPath, cancellationToken)
               ?? Task.FromException(
                   new InvalidOperationException("Unexpected Bilibili file request."));
    }
}
