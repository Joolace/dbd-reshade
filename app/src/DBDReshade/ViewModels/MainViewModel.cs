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
        LogService log)
    {
        _updater = updater;
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
            await _sync.SyncAsync();
        }
        catch (Exception ex)
        {
            _log.Warning($"Preset sync failed (working offline): {ex.Message}");
        }
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
}
