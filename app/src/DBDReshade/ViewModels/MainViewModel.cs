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
    private readonly LogService _log;

    public MainViewModel(
        InstallerViewModel installer,
        PresetManagerViewModel manager,
        CopyPresetsViewModel copy,
        SettingsViewModel settings,
        GitHubService github,
        PresetSyncService sync,
        TranslationSyncService translations,
        LogService log)
    {
        _github = github;
        _sync = sync;
        _translations = translations;
        _log = log;

        // Glyphs are Segoe Fluent Icons code points.
        Pages = new ObservableCollection<NavItem>
        {
            new("Nav.Install", "", installer),
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
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
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

        await Task.WhenAll(CheckForUpdateAsync(), SyncPresetsQuietlyAsync(), SyncTranslationsQuietlyAsync());
    }

    private async Task CheckForUpdateAsync()
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

    [RelayCommand]
    private void OpenUpdate() => Shell.Open(AvailableUpdate?.Url);

    [RelayCommand]
    private void OpenLink(string url) => Shell.Open(url);

    [RelayCommand]
    private void OpenDataFolder() => Shell.Open(AppPaths.DataRoot);

    [RelayCommand]
    private void ClearLog() => _log.Entries.Clear();
}
