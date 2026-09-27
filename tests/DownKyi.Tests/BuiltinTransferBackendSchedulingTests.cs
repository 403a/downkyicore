using DownKyi.Services.Download;

namespace DownKyi.Tests;

public sealed class BuiltinTransferBackendSchedulingTests
{
    [Theory]
    [InlineData(8 * 1024 * 1024, 1)]
    [InlineData(20 * 1024 * 1024, 1)]
    [InlineData(64 * 1024 * 1024, 4)]
    [InlineData(100 * 1024 * 1024, 5)]
    public void KnownSizeUsesFixedTwentyMegabyteSegments(
        long expectedBytes,
        int expectedSegmentCount)
    {
        var segmentCount = BuiltinTransferBackend.CalculateSegmentCount(expectedBytes);

        Assert.Equal(expectedSegmentCount, segmentCount);
    }

    [Fact]
    public void UnknownSizeIsReportedAfterRangeProbe()
    {
        var segmentCount = BuiltinTransferBackend.CalculateSegmentCount(expectedBytes: 0);

        Assert.Equal(0, segmentCount);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    public void InvalidInputsAreRejected(long expectedBytes, long segmentSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuiltinTransferBackend.CalculateSegmentCount(expectedBytes, segmentSize));
    }
}
