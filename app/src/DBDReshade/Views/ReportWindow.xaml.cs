using System.Windows;
using DBDReshade.Helpers;
using DBDReshade.Services;
using DBDReshade.ViewModels;

namespace DBDReshade.Views;

public partial class ReportWindow : Window
{
    /// <summary>Set once at start-up (App.xaml.cs).</summary>
    public static ReportService? Service { get; set; }

    private ReportWindow(ReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Owner = Application.Current.MainWindow is { IsLoaded: true } main && main != this ? main : null;

        SourceInitialized += (_, _) => WindowTheme.SetTitleBar(this, ThemeManager.IsDark);
        viewModel.RequestClose += (_, _) => Close();
    }

    /// <summary>Opens "Report a problem". Without the report service, falls back to Discord + the logs folder.</summary>
    /// <param name="prefill">Text to start the description with (e.g. the error the app just showed).</param>
    public static void Open(string? prefill = null)
    {
        if (Service is null || !ReportService.IsAvailable)
        {
            Dialogs.Info(Loc.Get("App.ReportBugInfo"));
            Shell.Open(AppInfo.DiscordUrl);
            Shell.Open(AppPaths.LogsDirectory);
            return;
        }
        new ReportWindow(new ReportViewModel(Service, prefill)).ShowDialog();
    }
}
