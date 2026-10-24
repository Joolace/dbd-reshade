using System.Windows;
using DBDReshade.Helpers;
using Microsoft.Win32;

namespace DBDReshade.Services;

public enum AppTheme { System, Light, Dark }

/// <summary>Swaps the color dictionary (Themes/Colors.*.xaml) at runtime and follows Windows when set to System.</summary>
public static class ThemeManager
{
    private static ResourceDictionary? _colors;
    private static bool _listening;

    public static AppTheme Selected { get; private set; } = AppTheme.System;

    public static bool IsDark { get; private set; } = true;

    public static void Apply(AppTheme theme)
    {
        Selected = theme;
        IsDark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => IsWindowsInDarkMode(),
        };

        var resources = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Colors.{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };

        // Colors go first so the control styles (merged after) resolve against them.
        if (_colors is not null) resources.Remove(_colors);
        resources.Insert(0, next);
        _colors = next;

        foreach (Window window in Application.Current.Windows)
            WindowTheme.SetTitleBar(window, IsDark);

        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Selected == AppTheme.System && e.Category == UserPreferenceCategory.General)
            Application.Current.Dispatcher.InvokeAsync(() => Apply(AppTheme.System));
    }

    private static bool IsWindowsInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return true;
        }
    }
}
