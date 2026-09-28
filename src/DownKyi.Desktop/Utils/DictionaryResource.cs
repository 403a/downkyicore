using System.Collections.Generic;
using Avalonia.Threading;

namespace DownKyi.Utils;

internal static class DictionaryResource
{
    /// <summary>
    /// 从资源获取字符串
    /// </summary>
    /// <param name="resourceKey"></param>
    /// <returns></returns>
    public static string GetString(string resourceKey)
    {
        var application = Avalonia.Application.Current;
        if (application == null)
        {
            return string.Empty;
        }

        var obj = Dispatcher.UIThread.Invoke(() =>
        {
            object? obj = null;
            application.TryGetResource(
                resourceKey,
                application.ActualThemeVariant,
                out obj);
            return obj;
        });
        return obj == null ? "" : (string)obj;
    }

    public static T Get<T>(string resourceKey)
    {
        var application = Avalonia.Application.Current
            ?? throw new KeyNotFoundException($"Resource '{resourceKey}' is unavailable before application initialization.");
        var obj = Dispatcher.UIThread.Invoke(() =>
        {
            object? obj = null;
            application.TryGetResource(
                resourceKey,
                application.ActualThemeVariant,
                out obj);
            return obj;
        });
        return obj is T value
            ? value
            : throw new KeyNotFoundException($"Resource '{resourceKey}' was not found or is not a {typeof(T).Name}.");
    }

    public static T? GetIfApplicationInitialized<T>(string resourceKey)
        where T : class
    {
        if (Avalonia.Application.Current == null)
        {
            return null;
        }

        return Get<T>(resourceKey);
    }
}
