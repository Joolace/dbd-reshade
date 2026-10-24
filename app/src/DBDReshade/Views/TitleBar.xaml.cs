using System.Windows;
using System.Windows.Controls;

namespace DBDReshade.Views;

/// <summary>
/// Title bar for windows using the ChromeWindow style. Dialogs (windows with an owner) only get the
/// close button; windows that can't be resized get no maximize button.
/// </summary>
public partial class TitleBar : UserControl
{
    private Window? _window;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null) return;
        _window = Window.GetWindow(this);
        if (_window is null) return;

        var isDialog = _window.Owner is not null;
        var canResize = _window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        MinimizeButton.Visibility = isDialog || _window.ResizeMode == ResizeMode.NoResize ? Visibility.Collapsed : Visibility.Visible;
        MaximizeButton.Visibility = isDialog || !canResize ? Visibility.Collapsed : Visibility.Visible;

        _window.StateChanged += (_, _) => UpdateState();
        UpdateState();
    }

    private void UpdateState()
    {
        if (_window is null) return;
        var maximized = _window.WindowState == WindowState.Maximized;
        MaximizeIcon.Text = maximized ? "" : "";

        // A maximized chromeless window extends past the screen by its resize border: pull the content in.
        if (_window.Content is FrameworkElement root)
            root.Margin = maximized ? new Thickness(8) : new Thickness(0);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(_window!);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (_window!.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(_window);
        else SystemCommands.MaximizeWindow(_window);
    }

    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(_window!);
}
