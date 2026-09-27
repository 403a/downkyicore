using DownKyi.Desktop;

namespace DownKyi.Tests;

public sealed class DesktopApplicationTests
{
    [Fact]
    public async Task WindowsUiOwnerRunsWorkInStaApartment()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

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
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = new InvalidOperationException("Synthetic UI startup failure.");
        var startup = DesktopApplication.RunOnWindowsStaThreadAsync(() => throw expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => startup);

        Assert.Same(expected, actual);
    }
}
