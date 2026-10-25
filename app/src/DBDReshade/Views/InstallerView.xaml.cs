using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DBDReshade.Services;
using DBDReshade.ViewModels;

namespace DBDReshade.Views;

public partial class InstallerView : UserControl
{
    public InstallerView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;

        // The buttons' labels change length with the window size and the language.
        InstallArea.SizeChanged += (_, _) => UpdateCompactButtons();
        Loaded += (_, _) =>
        {
            Loc.Changed += OnLanguageChanged;
            UpdateCompactButtons();
        };
        // The view is recreated each time the page is opened: don't leave the old one subscribed.
        Unloaded += (_, _) => Loc.Changed -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(UpdateCompactButtons), System.Windows.Threading.DispatcherPriority.Loaded);

    // Space opens the preview full screen, as in the original tool (except while typing in a text box).
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.FocusedElement is TextBox) return;
        if (DataContext is not InstallerViewModel vm) return;

        vm.ShowFullscreenCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Keeps the three buttons on one line: if they don't fit with their labels, "Try on maps" and
    /// "Launch game" show only their icon (their tooltip still explains them) and Install gets narrower.
    /// </summary>
    private void UpdateCompactButtons()
    {
        var available = InstallArea.ActualWidth;
        if (available <= 0) return;

        SetCompact(false);
        InstallButtons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetCompact(InstallButtons.DesiredSize.Width > available);
    }

    private void SetCompact(bool compact)
    {
        var text = compact ? Visibility.Collapsed : Visibility.Visible;
        var iconMargin = compact ? new Thickness(0) : new Thickness(0, 0, 10, 0);

        TryOnMapsText.Visibility = text;
        LaunchText.Visibility = text;
        TryOnMapsIcon.Margin = iconMargin;
        LaunchIcon.Margin = iconMargin;
        InstallButton.MinWidth = compact ? 0 : 200;
    }
}
