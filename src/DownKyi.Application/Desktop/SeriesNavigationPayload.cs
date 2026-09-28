namespace DownKyi.Application.Desktop;

public sealed record SeriesNavigationPayload
{
    public SeriesNavigationPayload(long mid, long seriesId)
    {
        if (mid <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mid), mid, "The uploader MID must be positive.");
        }

        if (seriesId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seriesId), seriesId, "The series ID must be positive.");
        }

        Mid = mid;
        SeriesId = seriesId;
    }

    public long Mid { get; }

    public long SeriesId { get; }
}

public sealed record SeasonNavigationPayload
{
    public SeasonNavigationPayload(long mid, long seasonId)
    {
        if (mid <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mid), mid, "The uploader MID must be positive.");
        }

        if (seasonId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seasonId), seasonId, "The season ID must be positive.");
        }

        Mid = mid;
        SeasonId = seasonId;
    }

    public long Mid { get; }

    public long SeasonId { get; }
}
