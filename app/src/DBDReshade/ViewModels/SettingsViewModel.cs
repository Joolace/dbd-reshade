using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>Theme, language and disk cleanup. Changes apply immediately and are saved to settings.json.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly StorageService _storage;
    private long _lastFreed = -1;
    private bool _refreshingLanguages;

    private readonly ChangelogService _changelog;
    private readonly OfflineService _offline;

    public SettingsViewModel(SettingsService settings, StorageService storage, ChangelogService changelog,
        OfflineService offline, NetworkService network, HotkeysViewModel hotkeys, OverlayViewModel overlay)
    {
        Hotkeys = hotkeys;
        Overlay = overlay;
        _changelog = changelog;
        _offline = offline;
        _isOnline = network.IsOnline;
        network.Changed += (_, online) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => IsOnline = online);
        _settings = settings;
        _storage = storage;
        _themeIndex = (int)ThemeManager.Selected;
        Languages = new ObservableCollection<LanguageOption>(Loc.Languages);
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == Loc.Current.Code);
        Loc.Changed += (_, _) =>
        {
            RefreshLanguages();
            UpdateFreedText();
            OnPropertyChanged(nameof(OfflineLastText));
        };
    }

    /// <summary>Built-in languages plus any downloaded from GitHub, with their completion.</summary>
    public ObservableCollection<LanguageOption> Languages { get; }

    /// <summary>Downloaded translations can add languages or change completion: rebuild the list.</summary>
    private void RefreshLanguages()
    {
        _refreshingLanguages = true;
        try
        {
            if (!Languages.SequenceEqual(Loc.Languages))
            {
                Languages.Clear();
                foreach (var language in Loc.Languages) Languages.Add(language);
            }
            SelectedLanguage = Languages.FirstOrDefault(l => l.Code == Loc.Current.Code);
        }
        finally
        {
            _refreshingLanguages = false;
        }
    }

    public string DataFolder => AppPaths.DataRoot;

    /// <summary>In-game shortcuts that switch preset.</summary>
    public HotkeysViewModel Hotkeys { get; }

    /// <summary>Overlay for streamers (OBS browser source).</summary>
    public OverlayViewModel Overlay { get; }

    // ---- Privacy ----------------------------------------------------------------------------

    /// <summary>The community ranking service is set up (AppInfo.StatsUrl).</summary>
    public bool RankingAvailable => StatsService.IsAvailable;

    /// <summary>Send anonymous install counts to the community ranking.</summary>
    public bool ShareUsage
    {
        get => _settings.Current.ShareUsage;
        set
        {
            if (_settings.Current.ShareUsage == value) return;
            _settings.Current.ShareUsage = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public string VersionText => AppInfo.VersionText;

    // ---- Theme ----------------------------------------------------------------------------

    /// <summary>0 = System, 1 = Light, 2 = Dark (same order as <see cref="AppTheme"/>).</summary>
    [ObservableProperty]
    private int _themeIndex;

    partial void OnThemeIndexChanged(int value)
    {
        if (value < 0) return;
        var theme = (AppTheme)value;
        ThemeManager.Apply(theme);
        _settings.Current.Theme = theme;
        _settings.Save();
    }

    // ---- Language -------------------------------------------------------------------------

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (_refreshingLanguages || value is null || value.Code == Loc.Current.Code) return;
        Loc.Apply(value.Code);
        _settings.Current.Language = value.Code;
        _settings.Save();
    }

    // ---- Storage --------------------------------------------------------------------------

    [ObservableProperty] private string _previewsSize = "…";
    [ObservableProperty] private string _mapsSize = "…";
    [ObservableProperty] private string _logsSize = "…";
    [ObservableProperty] private string _tempSize = "…";
    [ObservableProperty] private string _offlineSize = "…";
    [ObservableProperty] private string _totalSize = "…";
    [ObservableProperty] private string? _freedText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearPreviewsCommand), nameof(ClearMapsCommand), nameof(ClearLogsCommand),
                                nameof(ClearTempCommand), nameof(ClearOfflineCommand), nameof(ClearAllCommand),
                                nameof(DownloadOfflineCommand))]
    private bool _isWorking;

    private bool CanClear() => !IsWorking;

    /// <summary>Called each time the settings page is opened.</summary>
    public async Task RefreshStorageAsync()
    {
        var info = await Task.Run(_storage.Measure);
        PreviewsSize = StorageService.FormatBytes(info.PreviewsBytes);
        MapsSize = StorageService.FormatBytes(info.MapsBytes);
        LogsSize = StorageService.FormatBytes(info.LogsBytes);
        TempSize = StorageService.FormatBytes(info.TempBytes);
        OfflineSize = StorageService.FormatBytes(info.OfflineBytes);
        TotalSize = StorageService.FormatBytes(info.TotalBytes);
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearPreviewsAsync() => ClearAsync(_storage.ClearPreviews);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearMapsAsync() => ClearAsync(_storage.ClearMaps);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearLogsAsync() => ClearAsync(_storage.ClearLogs);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearTempAsync() => ClearAsync(_storage.ClearTemp);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearOfflineAsync() => ClearAsync(_storage.ClearOffline);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearAllAsync() => ClearAsync(_storage.ClearAll);

    // ---- Offline use ------------------------------------------------------------------------

    private CancellationTokenSource? _offlineCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadOfflineCommand))]
    private bool _isOnline = true;

    [ObservableProperty] private bool _isDownloadingOffline;
    [ObservableProperty] private double _offlineProgress;
    [ObservableProperty] private string? _offlineStatus;

    /// <summary>"Last downloaded: 27 October 2026", or that it was never done.</summary>
    public string OfflineLastText => _settings.Current.OfflineDownloadedUtc is { } last
        ? Loc.Format("Offline.Last", last.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentUICulture))
        : Loc.Get("Offline.Never");

    private bool CanDownloadOffline() => IsOnline && !IsWorking;

    [RelayCommand(CanExecute = nameof(CanDownloadOffline))]
    private async Task DownloadOfflineAsync()
    {
        if (!Dialogs.Confirm(Loc.Get("Offline.Confirm"))) return;

        IsWorking = true;
        IsDownloadingOffline = true;
        OfflineProgress = 0;
        var cts = _offlineCts = new CancellationTokenSource();
        try
        {
            var failed = await _offline.DownloadAllAsync(new Progress<OfflineProgress>(p =>
            {
                OfflineProgress = p.Overall * 100;
                OfflineStatus = p.Detail is { Length: > 0 } detail ? $"{Loc.Get(p.StepKey)} · {detail}" : Loc.Get(p.StepKey);
            }), cts.Token);

            _settings.Current.OfflineDownloadedUtc = DateTime.UtcNow;
            _settings.Save();
            OnPropertyChanged(nameof(OfflineLastText));
            OfflineStatus = failed == 0 ? Loc.Get("Offline.Done") : Loc.Format("Offline.DoneWithErrors", failed);
        }
        catch (OperationCanceledException)
        {
            OfflineStatus = Loc.Get("Offline.Cancelled");
        }
        finally
        {
            _offlineCts = null;
            IsDownloadingOffline = false;
            IsWorking = false;
            await RefreshStorageAsync();
        }
    }

    [RelayCommand]
    private void CancelOffline() => _offlineCts?.Cancel();

    private async Task ClearAsync(Func<long> clear)
    {
        IsWorking = true;
        try
        {
            _lastFreed = await Task.Run(clear);
            UpdateFreedText();
            await RefreshStorageAsync();
        }
        finally
        {
            IsWorking = false;
        }
    }

    private void UpdateFreedText() =>
        FreedText = _lastFreed < 0 ? null : Loc.Format("Set.Freed", StorageService.FormatBytes(_lastFreed));

    // ---- Links ----------------------------------------------------------------------------

    [RelayCommand]
    private void OpenDataFolder() => Shell.Open(AppPaths.DataRoot);

    /// <summary>Opens one of the folders listed under Storage (created first if it doesn't exist yet).</summary>
    [RelayCommand]
    private void OpenStorageFolder(string? which)
    {
        var folder = which switch
        {
            "previews" => AppPaths.PreviewsDirectory,
            "maps" => AppPaths.MapsDirectory,
            "logs" => AppPaths.LogsDirectory,
            "temp" => AppPaths.TempDirectory,
            "offline" => AppPaths.OfflineDirectory,
            _ => AppPaths.DataRoot,
        };
        try { Directory.CreateDirectory(folder); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Shell.Open(folder);
    }

    /// <summary>Opens the "What's new" window.</summary>
    [RelayCommand]
    private void ShowWhatsNew() => Views.WhatsNewWindow.Open(_changelog);

    /// <summary>Opens a link from the About card (GitHub, website, Discord...).</summary>
    [RelayCommand]
    private void OpenLink(string? url) => Shell.Open(url);

    // ---- About ----------------------------------------------------------------------------

    public string DeveloperName => AppInfo.DeveloperName;
    public string DeveloperUrl => AppInfo.DeveloperUrl;
    public string RepoUrl => AppInfo.RepoUrl;
    public string WebsiteUrl => AppInfo.WebsiteUrl;
    public string DiscordUrl => AppInfo.DiscordUrl;
    public string TwitchUrl => AppInfo.TwitchUrl;
    public string InstagramUrl => AppInfo.InstagramUrl;
    public string ReleasesUrl => AppInfo.ReleasesUrl;
    public string LicenseUrl => AppInfo.LicenseUrl;
    public string LicenseName => AppInfo.LicenseName;
    public string ReShadeUrl => "https://reshade.me";

    /// <summary>"v2.0.3 · .NET 8.0 · Windows 11 (64-bit)"</summary>
    public string BuildText =>
        $"{AppInfo.VersionText} · .NET {Environment.Version.ToString(2)} · {(Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10")} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})";

    [RelayCommand]
    private void OpenRepository() => Shell.Open(AppInfo.RepoUrl);

    [RelayCommand]
    private void ReportBug() => Shell.ReportBug();

    [RelayCommand]
    private void HelpTranslate() => Shell.Open(AppInfo.TranslateGuideUrl);

    [RelayCommand]
    private void OpenTranslationsFolder() => Shell.Open(AppPaths.TranslationsDirectory);
}
