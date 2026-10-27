using System.Windows;
using DBDReshade.Helpers;
using DBDReshade.Services;
using DBDReshade.ViewModels;

namespace DBDReshade.Views;

public partial class WhatsNewWindow : Window
{
    private static WhatsNewWindow? _open;

    private WhatsNewWindow(WhatsNewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Owner = Application.Current.MainWindow;

        SourceInitialized += (_, _) => WindowTheme.SetTitleBar(this, ThemeManager.IsDark);
        viewModel.RequestClose += (_, _) => Close();
        Loaded += async (_, _) => await viewModel.LoadAsync();
        Closed += (_, _) => _open = null;
    }

    /// <summary>Shows the "What's new" window (or brings it to the front if it's already open).</summary>
    public static void Open(ChangelogService changelog, bool justUpdated = false)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new WhatsNewWindow(new WhatsNewViewModel(changelog, justUpdated));
        _open.Show();
        _open.Activate();
    }
}
