using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Models;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>Sidebar entry; the title is a string-resource key so it follows the language.</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(string titleKey, string glyph, ObservableObject page)
    {
        TitleKey = titleKey;
        Glyph = glyph;
        Page = page;
        _title = Loc.Get(titleKey);
    }

    public string TitleKey { get; }
    public string Glyph { get; }
    public ObservableObject Page { get; }

    [ObservableProperty]
    private string _title;

    public void Refresh() => Title = Loc.Get(TitleKey);
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly GitHubService _github;
    private readonly PresetSyncService _sync;
    private readonly TranslationSyncService _translations;
    private readonly UpdateService _updater;
    private readonly LogService _log;
    private readonly PresetManagerViewModel _manager;
    private readonly ChangelogService _changelog;
    private readonly SettingsService _settings;
    private readonly NetworkService _network;
    private readonly InstallerViewModel _installer;

    /// <summary>Completes once the start-up screen is gone (windows can then be shown on top of the app).</summary>
    private readonly TaskCompletionSource _startupDone = new();

    public MainViewModel(
        InstallerViewModel installer,
        PresetManagerViewModel manager,
        CopyPresetsViewModel copy,
        EditorViewModel editor,
        SettingsViewModel settings,
        GitHubService github,
        PresetSyncService sync,
        TranslationSyncService translations,
        UpdateService updater,
        LogService log,
        ChangelogService changelog,
        SettingsService settingsService,
        NetworkService network)
    {
        _network = network;
        _installer = installer;
        _isOffline = !network.IsOnline;
        network.Changed += (_, online) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => OnNetworkChanged(online));
        _changelog = changelog;
        _settings = settingsService;
        _updater = updater;
        _manager = manager;
        _github = github;
        _sync = sync;
        _translations = translations;
        _log = log;

        // Glyphs are Segoe Fluent Icons code points.
        Pages = new ObservableCollection<NavItem>
        {
            new("Nav.Install", "", installer),
            new("Nav.Editor", "", editor),
            new("Nav.Manager", "", manager),
            new("Nav.Copy", "", copy),
            new("Nav.Settings", "", settings),
        };
        _selectedPage = Pages[0];

        Loc.Changed += (_, _) =>
        {
            foreach (var page in Pages) page.Refresh();
        };
    }

    public ObservableCollection<NavItem> Pages { get; }

    public ObservableCollection<LogEntry> Log => _log.Entries;

    public string VersionText => AppInfo.VersionText;

    public string Author => AppInfo.Author;

    [ObservableProperty]
    private NavItem _selectedPage;

    partial void OnSelectedPageChanged(NavItem value)
    {
        // Disk usage can change at any time (previews download in the background): re-measure on open.
        if (value.Page is SettingsViewModel settings) _ = settings.RefreshStorageAsync();
        if (value.Page is EditorViewModel editor) _ = editor.ActivateAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    private ReleaseInfo? _availableUpdate;

    public bool IsUpdateAvailable => AvailableUpdate is not null;

    [ObservableProperty]
    private bool _isLogExpanded = true;

    // ---- Connection ---------------------------------------------------------------------------

    /// <summary>No internet: a banner says what still works.</summary>
    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryConnectionCommand))]
    private bool _isCheckingConnection;

    private void OnNetworkChanged(bool online)
    {
        var wasOffline = IsOffline;
        IsOffline = !online;
        // Back online: catch up on what was skipped (new presets, translations, app updates).
        if (wasOffline && online)
            _ = Task.WhenAll(SyncPresetsQuietlyAsync(), SyncTranslationsQuietlyAsync(), CheckForUpdateAsync());
    }

    [RelayCommand(CanExecute = nameof(CanRetryConnection))]
    private async Task RetryConnectionAsync()
    {
        IsCheckingConnection = true;
        try
        {
            await _network.CheckAsync();
            await Task.Delay(400); // let the banner visibly react, even when the answer is instant
        }
        finally
        {
            IsCheckingConnection = false;
        }
    }

    private bool CanRetryConnection() => !IsCheckingConnection;

    /// <summary>Opens the "What's new" window (sidebar button).</summary>
    [RelayCommand]
    private void ShowWhatsNew() => Views.WhatsNewWindow.Open(_changelog);

    /// <summary>
    /// After an update, opens "What's new" once. Not on the very first run (nothing changed for a new user):
    /// that version is just remembered.
    /// </summary>
    public void ShowWhatsNewIfUpdated()
    {
        _startupDone.TrySetResult();
        var current = AppInfo.Version.ToString(3);
        var last = _settings.Current.LastSeenVersion;
        if (last == current) return;

        _settings.Current.LastSeenVersion = current;
        _settings.Save();
        if (last is null && _settings.IsFirstRun) return;


        _log.Info($"Updated to {AppInfo.VersionText}: showing what's new.");
        Views.WhatsNewWindow.Open(_changelog, justUpdated: true);
    }

    /// <summary>Runs once when the main window has been shown.</summary>
    public async Task InitializeAsync()
    {
        _log.Info($"DBD Reshade {AppInfo.VersionText} started. Data folder: {AppPaths.DataRoot}");
        if (AppInfo.IsTestBranch)
            _log.Warning($"Test mode: presets, translations and maps are read from the GitHub branch \"{AppInfo.GitHubBranch}\".");

        // The update check already ran (or is still running) behind the start-up screen.
        await Task.WhenAll(SyncPresetsQuietlyAsync(), SyncTranslationsQuietlyAsync());
    }

    /// <summary>Started by the start-up screen; sets <see cref="AvailableUpdate"/> when a newer release exists.</summary>
    public async Task CheckForUpdateAsync()
    {
        try
        {
            var release = await _github.GetLatestReleaseAsync();
            if (release?.Version is { } latest && latest > AppInfo.Version)
            {
                AvailableUpdate = release;
                _log.Warning($"A new version is available: {release.Tag}");
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Update check failed: {ex.Message}");
        }
    }

    private async Task SyncPresetsQuietlyAsync()
    {
        try
        {
            var result = await _sync.SyncAsync();
            if (result.Added.Count > 0 && _settings.Current.ShowNewPresetsWindow)
                await ShowNewPresetsAsync(result.Added);
        }
        catch (Exception ex)
        {
            _log.Warning($"Preset sync failed (working offline): {ex.Message}");
        }
    }

    /// <summary>
    /// "N new presets" window, after the start-up screen and after "What's new" (one window at a time).
    /// Not on the very first run: then every preset is new.
    /// </summary>
    private async Task ShowNewPresetsAsync(IReadOnlyList<string> added)
    {
        if (_settings.IsFirstRun) return;
        await _startupDone.Task;
        await Views.WhatsNewWindow.WaitClosedAsync();

        var presets = added
            .Select(name => _installer.Presets.FirstOrDefault(p => string.Equals(p.FileName, name, StringComparison.OrdinalIgnoreCase)))
            .OfType<Preset>().ToList();
        if (presets.Count == 0) return;

        var vm = new NewPresetsViewModel(presets, _github, _settings,
            show: preset => { SelectedPage = Pages[0]; _installer.ShowPreset(preset); },
            showAllNew: () => { SelectedPage = Pages[0]; _installer.ShowNewPresetsCommand.Execute(null); });
        new Views.NewPresetsWindow(vm).ShowDialog();
    }

    private async Task SyncTranslationsQuietlyAsync()
    {
        try
        {
            await _translations.SyncAsync();
        }
        catch (Exception ex)
        {
            // No translations folder on GitHub yet, or offline: the built-in translations are used.
            _log.Info($"Translation update skipped: {ex.Message}");
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    private bool _isUpdating;

    [ObservableProperty]
    private double _updateProgress;

    private bool CanUpdate() => !IsUpdating && AvailableUpdate is not null;

    /// <summary>Downloads and installs the new version, then closes so it can take over.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateNowAsync()
    {
        var release = AvailableUpdate!;
        IsUpdating = true;
        UpdateProgress = 0;
        try
        {
            if (await _updater.InstallAsync(release, new System.Progress<double>(p => UpdateProgress = p * 100)))
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }
            Shell.Open(release.Url); // nothing to install automatically: the release page explains
        }
        catch (Exception ex)
        {
            _log.Error($"Update failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Update.Failed", ex.Message));
            Shell.Open(release.Url);
        }
        IsUpdating = false;
    }

    [RelayCommand]
    private void OpenUpdate() => Shell.Open(AvailableUpdate?.Url);

    [RelayCommand]
    private void OpenLink(string url) => Shell.Open(url);

    [RelayCommand]
    private void OpenDataFolder() => Shell.Open(AppPaths.DataRoot);

    [RelayCommand]
    private void ReportBug() => Shell.ReportBug();

    [RelayCommand]
    private void ClearLog() => _log.Entries.Clear();

    /// <summary>Files dropped on the window: preset packages and .ini files go to the Preset Manager.</summary>
    public void ImportDroppedFiles(IReadOnlyList<string> files)
    {
        var presets = files.Where(f => PresetPackageService.IsPackage(f) || f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).ToList();
        if (presets.Count == 0)
        {
            Dialogs.Info(Loc.Get("Man.DropUnsupported"));
            return;
        }

        SelectedPage = Pages.First(p => p.Page == _manager);
        _manager.ImportFiles(presets);
    }
}
