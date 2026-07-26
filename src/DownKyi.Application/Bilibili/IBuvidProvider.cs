namespace DownKyi.Application.Bilibili;

public sealed record BilibiliBuvid(string Buvid3, string Buvid4);

public interface IBuvidProvider
{
    Task<BilibiliBuvid> GetAsync(CancellationToken cancellationToken);
}
