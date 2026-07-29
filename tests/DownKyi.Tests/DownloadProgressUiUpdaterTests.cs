using DownKyi.Services.Download;

namespace DownKyi.Tests;

public sealed class DownloadProgressUiUpdaterTests
{
    [Fact]
    public void ProgressSamplesAreBoundedAndCompletionIsAlwaysPublished()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var updater = new DownloadProgressUiUpdater(clock, TimeSpan.FromMilliseconds(100));
        var published = new List<DownKyi.Domain.Downloads.DownloadProgress>();
        for (var millisecond = 0; millisecond < 1000; millisecond++)
        {
            if (updater.TryCreate(
                    millisecond / 10d,
                    millisecond,
                    1000,
                    millisecond,
                    out var progress))
            {
                published.Add(progress);
            }

            clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.True(updater.TryCreate(100, 1000, 1000, 5000, out var completed));
        published.Add(completed);

        Assert.Equal(11, published.Count);
        Assert.Equal(100, completed.Percentage);
        Assert.Equal(5000, completed.BytesPerSecond);
        Assert.Equal(1000, completed.DownloadedBytes);
        Assert.Equal(1000, completed.TotalBytes);
    }

    [Fact]
    public void SuppressedSamplesDoNotReplaceLastPublishedDomainProgress()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var updater = new DownloadProgressUiUpdater(clock, TimeSpan.FromSeconds(1));

        Assert.True(updater.TryCreate(1, 1, 100, 100, out var first));
        Assert.False(updater.TryCreate(2, 2, 100, 10_000, out _));

        Assert.Equal(100, first.BytesPerSecond);
        Assert.Equal(1, first.Percentage);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
