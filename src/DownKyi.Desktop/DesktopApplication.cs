using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia;
using DownKyi.Platform;

namespace DownKyi.Desktop;

public static class DesktopApplication
{
    public static async Task RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (await ProcessRestartLauncher.RunHelperIfRequestedAsync(args).ConfigureAwait(false))
        {
            return;
        }

        var appBuilder = BuildAvaloniaApp();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                await RunOnWindowsStaThreadAsync(
                        () => appBuilder.StartWithClassicDesktopLifetime(args))
                    .ConfigureAwait(false);
            }
            else
            {
                appBuilder.StartWithClassicDesktopLifetime(args);
            }
        }
        finally
        {
            if (appBuilder.Instance is IAsyncDisposable application)
            {
                await application.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The UI thread boundary must preserve every startup failure on the owning task.")]
    [SupportedOSPlatform("windows")]
    internal static Task RunOnWindowsStaThreadAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = new Thread(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = false,
            Name = "DownKyi UI"
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        return completion.Task;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .LogToTrace()
#endif
            ;
    }
}
