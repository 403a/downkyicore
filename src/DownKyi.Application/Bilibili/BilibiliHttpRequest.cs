namespace DownKyi.Application.Bilibili;

public sealed record BilibiliHttpRequest
{
    public BilibiliHttpRequest(
        string requestAddress,
        string? referer = null,
        bool includeCredentials = true,
        bool includeBuvid = true,
        int attempts = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestAddress);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        RequestAddress = requestAddress;
        Referer = referer;
        IncludeCredentials = includeCredentials;
        IncludeBuvid = includeBuvid;
        Attempts = attempts;
    }

    public string RequestAddress { get; }

    public string? Referer { get; }

    public bool IncludeCredentials { get; }

    public bool IncludeBuvid { get; }

    public int Attempts { get; }
}
