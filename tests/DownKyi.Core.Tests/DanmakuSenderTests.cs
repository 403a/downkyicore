using DownKyi.Core.BiliApi.BiliUtils;

namespace DownKyi.Core.Tests;

public sealed class DanmakuSenderTests
{
    [Theory]
    [InlineData(1, "83dcefb7")]
    [InlineData(123456, "972d361")]
    [InlineData(123456789, "cbf43926")]
    public void UserIdToMidHashUsesStableCrc32Values(long userId, string expected)
    {
        Assert.Equal(expected, DanmakuSender.GetMidHash(userId));
    }

    [Fact]
    public void LookupRejectsCancellationBeforeCpuSearchStarts()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => DanmakuSender.FindDanmakuSender("ffffffff", cancellation.Token));
    }
}
