using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DBDReshade.Models;
using Microsoft.Win32;

namespace DBDReshade.Helpers;

/// <summary>Thin wrapper over the standard dialogs so view models stay free of UI types.</summary>
public static class Dialogs
{
    private static Window? Owner => Application.Current?.MainWindow;

    public static void Info(string message, string title = "DBD Reshade") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warning(string message, string title = "DBD Reshade") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Error(string message, string title = "DBD Reshade") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static bool Confirm(string message, string title = "DBD Reshade") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <returns>true = Yes, false = No, null = Cancel.</returns>
    public static bool? YesNoCancel(string message, string title = "DBD Reshade") =>
        Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public static string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;
        return ShowDialog(dialog) ? dialog.FolderName : null;
    }

    public static string? PickFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);

    private static bool ShowDialog(CommonDialog dialog) =>
        (Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
}

public static class Shell
{
    public static void Open(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // No default browser / folder vanished: nothing useful to do.
        }
    }
}

public static class ImageLoader
{
    /// <summary>
    /// Loads an image fully into memory (so the file isn't locked) and freezes it,
    /// which makes it safe to create on a background thread.
    /// </summary>
    public static BitmapImage Load(string path, int decodePixelWidth = 0)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = new Uri(path, UriKind.Absolute);
        if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public static class WindowTheme
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Matches the native title bar to the app theme (Windows 10 20H1+ and 11).</summary>
    public static void SetTitleBar(Window window, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return; // not shown yet: SourceInitialized will call us again

            var value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int)); // older Windows 10 builds
        }
        catch
        {
            // Purely cosmetic.
        }
    }
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is string s ? !string.IsNullOrWhiteSpace(s) : value is not null;
        return hasValue ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Picks the log line color from the current theme (LogInfoBrush, LogErrorBrush…).</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            LogLevel.Success => "LogSuccessBrush",
            LogLevel.Warning => "LogWarningBrush",
            LogLevel.Error => "LogErrorBrush",
            _ => "LogInfoBrush",
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
