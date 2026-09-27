using System.Runtime.Versioning;
using DownKyi.Desktop;

namespace DownKyi.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class DesktopApplicationTests
{
    [Fact]
    public async Task WindowsUiOwnerRunsWorkInStaApartment()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var uiThreadId = 0;
        var apartmentState = ApartmentState.Unknown;

        await DesktopApplication.RunOnWindowsStaThreadAsync(() =>
        {
            uiThreadId = Environment.CurrentManagedThreadId;
            apartmentState = Thread.CurrentThread.GetApartmentState();
        });

        Assert.NotEqual(callerThreadId, uiThreadId);
        Assert.Equal(ApartmentState.STA, apartmentState);
    }

    [Fact]
    public async Task WindowsUiOwnerPropagatesStartupFailure()
    {
        var expected = new InvalidOperationException("Synthetic UI startup failure.");
        var startup = DesktopApplication.RunOnWindowsStaThreadAsync(() => throw expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => startup);

        Assert.Same(expected, actual);
    }
}
