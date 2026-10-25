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

    /// <summary>The start-up screen stays at least this long, so it never just flickers.</summary>
    private static readonly TimeSpan SplashMinimum = TimeSpan.FromMilliseconds(1200);

    /// <summary>The update check may delay the start this long at most (e.g. when offline).</summary>
    private static readonly TimeSpan UpdateCheckLimit = TimeSpan.FromSeconds(3);

    protected override async void OnStartup(StartupEventArgs e)
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

        // Start-up screen first, then build everything behind it.
        var shownAt = DateTime.UtcNow;
        var splash = new SplashWindow();
        splash.Show();
        await Step(splash, "Splash.Loading");

        var github = new GitHubService();
        var repository = new PresetRepository(settings);
        repository.Reload();

        var sync = new PresetSyncService(github, repository, _log);
        var locator = new GameLocator(_log);
        var reshade = new ReShadeService(_log);
        var storage = new StorageService(repository, _log);
        var translations = new TranslationSyncService(github, _log);
        var maps = new MapService(github, _log);
        var shaders = new ShaderService(_log);
        var mapPreview = new MapPreviewService(reshade, shaders, _log);
        var updater = new UpdateService(_log);
        var packages = new PresetPackageService(_log);

        await Step(splash, "Splash.Game");
        var installer = new InstallerViewModel(repository, sync, locator, reshade, shaders, github, settings, _log, maps, mapPreview, new GameLauncher(_log), new BackupService(_log));
        var main = new MainViewModel(
            installer,
            new PresetManagerViewModel(repository, sync, packages, _log),
            new CopyPresetsViewModel(repository, _log),
            new EditorViewModel(repository, maps, mapPreview, () => installer.SelectedInstallation, packages, _log),
            new SettingsViewModel(settings, storage),
            github, sync, translations, updater, _log);

        // Like Discord: look for a new version before opening, without making anyone wait for long.
        await Step(splash, "Splash.Updates");
        var updateCheck = main.CheckForUpdateAsync();
        await Task.WhenAny(updateCheck, Task.Delay(UpdateCheckLimit));
        if (main.AvailableUpdate is { } update)
        {
            splash.Status = Loc.Format("Splash.UpdateFound", update.Tag);
            await Task.Delay(700);
        }

        splash.Status = Loc.Get("Splash.Starting");
        var remaining = SplashMinimum - (DateTime.UtcNow - shownAt);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);

        var window = new MainWindow { DataContext = main };
        MainWindow = window; // before the splash closes, or closing it would end the app
        window.Show();
        await splash.CloseWithFadeAsync();
    }

    /// <summary>Shows a status line and lets the start-up screen repaint before the next step.</summary>
    private static async Task Step(SplashWindow splash, string key)
    {
        splash.Status = Loc.Get(key);
        await Task.Delay(40);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error($"Unexpected error: {e.Exception}");
        Dialogs.Error(Loc.Format("App.Error", e.Exception.Message, AppPaths.LogsDirectory));
        e.Handled = true;
    }
}
