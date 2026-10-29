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
    /// <summary>Opens "Report a problem" (it becomes a GitHub issue; falls back to Discord without the service).</summary>
    public static void ReportBug() => Views.ReportWindow.Open();

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
            {
                // Explorer reads its argument as a path: an address with "?…" or a port (localhost:7373)
                // isn't one, and it would open the Documents folder instead. An Internet shortcut (.url)
                // always opens in the default browser.
                var argument = target;
                if (uri.Scheme is "http" or "https")
                {
                    var folder = Path.Combine(Path.GetTempPath(), "DBDReshade");
                    Directory.CreateDirectory(folder);
                    argument = Path.Combine(folder, $"link-{Guid.NewGuid():N}.url"); // one per click: Explorer reads it a moment later
                    File.WriteAllText(argument, $"[InternetShortcut]\r\nURL={uri.AbsoluteUri}\r\n");
                }
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { argument }, UseShellExecute = false });
            }
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
        // From a stream, not a Uri: a file name with "#" or "%" would be read as a URL fragment / escape.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        // No IgnoreImageCache here: with a stream there is no Uri, and WPF would throw trying to remove a null
        // key from its image cache. The cache only applies to images loaded by Uri anyway.
        image.StreamSource = stream;
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

/// <summary>
/// A box that records a keyboard shortcut: click it and press a key (with Ctrl / Shift / Alt if wanted).
/// Backspace or Delete clears it, Esc leaves it as it was. Values are Windows virtual-key codes, as ReShade uses.
/// </summary>
public sealed class HotkeyBox : System.Windows.Controls.TextBox
{
    public static readonly DependencyProperty KeyCodeProperty = DependencyProperty.Register(nameof(KeyCode), typeof(int),
        typeof(HotkeyBox), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Refresh()));
    public static readonly DependencyProperty CtrlProperty = DependencyProperty.Register(nameof(Ctrl), typeof(bool),
        typeof(HotkeyBox), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Refresh()));
    public static readonly DependencyProperty ShiftProperty = DependencyProperty.Register(nameof(Shift), typeof(bool),
        typeof(HotkeyBox), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Refresh()));
    public static readonly DependencyProperty AltProperty = DependencyProperty.Register(nameof(Alt), typeof(bool),
        typeof(HotkeyBox), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Refresh()));

    public int KeyCode { get => (int)GetValue(KeyCodeProperty); set => SetValue(KeyCodeProperty, value); }
    public bool Ctrl { get => (bool)GetValue(CtrlProperty); set => SetValue(CtrlProperty, value); }
    public bool Shift { get => (bool)GetValue(ShiftProperty); set => SetValue(ShiftProperty, value); }
    public bool Alt { get => (bool)GetValue(AltProperty); set => SetValue(AltProperty, value); }

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = System.Windows.Input.Cursors.Hand;
        GotKeyboardFocus += (_, _) => Refresh();
        LostKeyboardFocus += (_, _) => Refresh();
        // Subscribed only while on screen: the static event would otherwise keep every removed row alive.
        Loaded += (_, _) => { Services.Loc.Changed += OnLanguageChanged; Refresh(); };
        Unloaded += (_, _) => Services.Loc.Changed -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
                or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
                or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
                or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin:
                return; // wait for the real key
            case System.Windows.Input.Key.Escape:
            case System.Windows.Input.Key.Tab:
                System.Windows.Input.Keyboard.ClearFocus();
                return;
            case System.Windows.Input.Key.Back or System.Windows.Input.Key.Delete:
                KeyCode = 0; Ctrl = Shift = Alt = false;
                return;
        }

        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        Ctrl = modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        Shift = modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        Alt = modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt);
        KeyCode = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        System.Windows.Input.Keyboard.ClearFocus();
    }

    private void Refresh() =>
        Text = KeyCode == 0
            ? Services.Loc.Get(IsKeyboardFocused ? "Hotkeys.PressKey" : "Hotkeys.NoKey")
            : Format(KeyCode, Ctrl, Shift, Alt);

    /// <summary>"Ctrl + Shift + F5"</summary>
    public static string Format(int vk, bool ctrl, bool shift, bool alt)
    {
        var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk);
        var name = key switch
        {
            >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 => ((int)(key - System.Windows.Input.Key.D0)).ToString(),
            >= System.Windows.Input.Key.NumPad0 and <= System.Windows.Input.Key.NumPad9 => "Num " + (int)(key - System.Windows.Input.Key.NumPad0),
            System.Windows.Input.Key.None => $"#{vk}",
            _ => key.ToString(),
        };
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        parts.Add(name);
        return string.Join(" + ", parts);
    }
}
