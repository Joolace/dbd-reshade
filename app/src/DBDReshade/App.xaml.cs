using System.Windows;
using System.Windows.Threading;
using DBDReshade.Helpers;
using DBDReshade.Services;
using DBDReshade.ViewModels;
using DBDReshade.Views;

namespace DBDReshade;

public partial class App : Application
{
    private LogService? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        AppPaths.Initialize();

        // Manual composition: the app is small enough not to need a DI container.
        _log = new LogService();
        var settings = new SettingsService();
        settings.Load();

        // Theme and language must be in place before any window is created.
        ThemeManager.Apply(settings.Current.Theme);
        Loc.Initialize();
        Loc.Apply(settings.Current.Language ?? Loc.DefaultCode);

        var github = new GitHubService();
        var repository = new PresetRepository(settings);
        repository.Reload();

        var sync = new PresetSyncService(github, repository, _log);
        var locator = new GameLocator(_log);
        var reshade = new ReShadeService(_log);
        var storage = new StorageService(repository, _log);
        var translations = new TranslationSyncService(github, _log);
        var maps = new MapService(github, _log);
        var mapPreview = new MapPreviewService(reshade, _log);

        var main = new MainViewModel(
            new InstallerViewModel(repository, sync, locator, reshade, github, settings, _log, maps, mapPreview),
            new PresetManagerViewModel(repository, sync, _log),
            new CopyPresetsViewModel(repository, _log),
            new SettingsViewModel(settings, storage),
            github, sync, translations, _log);

        var window = new MainWindow { DataContext = main };
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error($"Unexpected error: {e.Exception}");
        Dialogs.Error(Loc.Format("App.Error", e.Exception.Message, AppPaths.LogsDirectory));
        e.Handled = true;
    }
}
