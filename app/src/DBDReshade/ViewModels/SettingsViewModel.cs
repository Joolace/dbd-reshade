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

    public SettingsViewModel(SettingsService settings, StorageService storage)
    {
        _settings = settings;
        _storage = storage;
        _themeIndex = (int)ThemeManager.Selected;
        Languages = new ObservableCollection<LanguageOption>(Loc.Languages);
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == Loc.Current.Code);
        Loc.Changed += (_, _) =>
        {
            RefreshLanguages();
            UpdateFreedText();
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
    [ObservableProperty] private string _totalSize = "…";
    [ObservableProperty] private string? _freedText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearPreviewsCommand), nameof(ClearMapsCommand), nameof(ClearLogsCommand),
                                nameof(ClearTempCommand), nameof(ClearAllCommand))]
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
    private Task ClearAllAsync() => ClearAsync(_storage.ClearAll);

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

    [RelayCommand]
    private void OpenRepository() => Shell.Open(AppInfo.RepoUrl);

    [RelayCommand]
    private void ReportBug() => Shell.ReportBug();

    [RelayCommand]
    private void HelpTranslate() => Shell.Open(AppInfo.TranslateGuideUrl);

    [RelayCommand]
    private void OpenTranslationsFolder() => Shell.Open(AppPaths.TranslationsDirectory);
}
