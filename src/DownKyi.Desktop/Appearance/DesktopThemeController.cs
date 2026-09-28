using Avalonia.Styling;
using Avalonia.Threading;
using DownKyi.Core.Settings;

namespace DownKyi.Desktop.Appearance;

internal sealed class DesktopThemeController
{
    private readonly ISettingsStore _settingsStore;

    public DesktopThemeController(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public void ApplySavedMode()
    {
        Apply(_settingsStore.Current.Basic.ThemeMode);
    }

    public bool SetMode(ThemeMode mode)
    {
        var themeVariant = ToThemeVariant(mode);
        var updated = _settingsStore.Update(settings => settings with
        {
            Basic = settings.Basic with { ThemeMode = mode }
        });
        if (updated.Basic.ThemeMode != mode)
        {
            return false;
        }

        Apply(themeVariant);
        return true;
    }

    internal static ThemeVariant ToThemeVariant(ThemeMode mode)
    {
        return mode switch
        {
            ThemeMode.None => ThemeVariant.Default,
            ThemeMode.Default => ThemeVariant.Default,
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported theme mode.")
        };
    }

    private static void Apply(ThemeMode mode)
    {
        Apply(ToThemeVariant(mode));
    }

    private static void Apply(ThemeVariant themeVariant)
    {
        Dispatcher.UIThread.VerifyAccess();
        var application = Avalonia.Application.Current
            ?? throw new InvalidOperationException("The Avalonia application must be initialized before applying a theme.");
        application.RequestedThemeVariant = themeVariant;
    }
}
