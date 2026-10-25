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
    /// <summary>The window in front (a dialog such as the map picker), else the main window.</summary>
    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

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

    public static IReadOnlyList<string> PickFiles(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = true };
        return ShowDialog(dialog) ? dialog.FileNames : Array.Empty<string>();
    }

    public static string? SaveFile(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, AddExtension = true };
        return ShowDialog(dialog) ? dialog.FileName : null;
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
    /// <summary>
    /// Bug reports go to Discord: explains what to send, then opens Discord and the logs folder
    /// so today's log file can be attached right away.
    /// </summary>
    public static void ReportBug()
    {
        Dialogs.Info(Services.Loc.Get("App.ReportBugInfo"));
        Open(AppInfo.DiscordUrl);
        Open(Services.AppPaths.LogsDirectory);
    }

    /// <summary>Opens Explorer with the file selected.</summary>
    public static void ShowInFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false });
        }
        catch
        {
            Open(Path.GetDirectoryName(path));
        }
    }

    public static void Open(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            // The app runs as administrator: links go through Explorer so the browser / Discord open
            // normally, not as administrator. Folders and files open directly.
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile)
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { target }, UseShellExecute = false });
            else
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

/// <summary>true ⇄ false, for bindings such as IsEnabled="{Binding IsBusy, Converter=…}".</summary>
public sealed class InverseBool : IValueConverter
{
    public static readonly InverseBool Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>true → Collapsed, false → Visible (static instance, no resource needed).</summary>
public sealed class InverseBoolToVisibility : IValueConverter
{
    public static readonly InverseBoolToVisibility Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
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

/// <summary>
/// Shows the first child that fits the available width (the others are kept but invisible), aligned right.
/// Used for button rows: a full version with labels first, then more compact ones. The choice is made
/// while measuring, so the layout never overflows or jumps for a frame while the window is resized.
/// </summary>
public sealed class FirstFitPanel : System.Windows.Controls.Panel
{
    private UIElement? _shown;

    protected override Size MeasureOverride(Size available)
    {
        _shown = null;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            if (_shown is null && child.DesiredSize.Width <= available.Width) _shown = child;
        }

        // Nothing fits: show the most compact version anyway.
        if (_shown is null && InternalChildren.Count > 0) _shown = InternalChildren[InternalChildren.Count - 1];
        if (_shown is null) return new Size();

        var width = double.IsInfinity(available.Width) ? _shown.DesiredSize.Width : Math.Min(_shown.DesiredSize.Width, available.Width);
        return new Size(width, _shown.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var shown = ReferenceEquals(child, _shown);
            // Opacity and hit-testing, not Visibility: changing Visibility here would start a new layout pass.
            child.Opacity = shown ? 1 : 0;
            child.IsHitTestVisible = shown;
            child.IsEnabled = shown; // hidden buttons can't be reached with Tab either

            var width = child.DesiredSize.Width;
            child.Arrange(shown
                ? new Rect(Math.Max(0, finalSize.Width - width), 0, width, finalSize.Height)
                : new Rect(0, 0, 0, 0));
        }
        return finalSize;
    }
}

/// <summary>
/// Accepts files dropped from Explorer. The app runs as administrator, and Windows blocks normal (OLE)
/// drag and drop from the non-elevated Explorer into it; the older WM_DROPFILES message can be allowed
/// through explicitly, which is what this does.
/// </summary>
public static class FileDrop
{
    private const int WM_DROPFILES = 0x0233;
    private const int WM_COPYDATA = 0x004A;
    private const int WM_COPYGLOBALDATA = 0x0049;
    private const uint MSGFLT_ALLOW = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeInfo);

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hwnd, bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint index, System.Text.StringBuilder? file, uint size);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr hDrop);

    /// <summary>Call from SourceInitialized (the window needs its handle).</summary>
    public static void Enable(Window window, Action<IReadOnlyList<string>> onDrop)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        foreach (var message in new uint[] { WM_DROPFILES, WM_COPYDATA, WM_COPYGLOBALDATA })
            ChangeWindowMessageFilterEx(hwnd, message, MSGFLT_ALLOW, IntPtr.Zero);
        DragAcceptFiles(hwnd, true);

        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr wParam, IntPtr _, ref bool handled) =>
        {
            if (msg != WM_DROPFILES) return IntPtr.Zero;

            var files = new List<string>();
            var count = DragQueryFile(wParam, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFile(wParam, i, null, 0);
                var buffer = new System.Text.StringBuilder((int)length + 1);
                DragQueryFile(wParam, i, buffer, (uint)buffer.Capacity);
                files.Add(buffer.ToString());
            }
            DragFinish(wParam);
            handled = true;

            // Let the drop finish before showing any dialog.
            window.Dispatcher.BeginInvoke(new Action(() => onDrop(files)));
            return IntPtr.Zero;
        });
    }
}
