namespace DownKyi.Application.Bilibili;

public interface IBilibiliApiClient
{
    Task<string> GetStringAsync(
        BilibiliHttpRequest request,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(
        BilibiliHttpRequest request,
        CancellationToken cancellationToken);

    Task DownloadFileAsync(
        BilibiliHttpRequest request,
        string destinationPath,
        CancellationToken cancellationToken);
}
