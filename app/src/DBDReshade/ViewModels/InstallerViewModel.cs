using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Models;
using DBDReshade.Services;
using DBDReshade.Views;

namespace DBDReshade.ViewModels;

public sealed partial class InstallerViewModel : ObservableObject
{
    private readonly PresetRepository _repository;
    private readonly PresetSyncService _sync;
    private readonly GameLocator _locator;
    private readonly ReShadeService _reshade;
    private readonly ShaderService _shaders;
    private readonly GitHubService _github;
    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly MapService _maps;
    private readonly MapPreviewService _mapPreview;
    private readonly GameLauncher _launcher;
    private readonly BackupService _backups;
    private readonly StatsService _stats;
    private readonly HotkeyService _hotkeys;
    private CancellationTokenSource? _previewCts;
    private string? _previewMessageKey;

    public InstallerViewModel(
        PresetRepository repository, PresetSyncService sync, GameLocator locator, ReShadeService reshade,
        ShaderService shaders, GitHubService github, SettingsService settings, LogService log, MapService maps, MapPreviewService mapPreview,
        GameLauncher launcher, BackupService backups, StatsService stats, HotkeyService hotkeys)
    {
        _hotkeys = hotkeys;
        _stats = stats;
        _launcher = launcher;
        _backups = backups;
        _maps = maps;
        _mapPreview = mapPreview;
        _repository = repository;
        _sync = sync;
        _locator = locator;
        _reshade = reshade;
        _shaders = shaders;
        _github = github;
        _settings = settings;
        _log = log;

        PresetsView = CollectionViewSource.GetDefaultView(Presets);
        PresetsView.Filter = o => o is Preset p && Matches(p);

        _repository.Changed += (_, _) => ReloadPresets();
        _repository.FavoritesChanged += (_, _) => { if (SelectedShow?.Value == ShowFavorites) PresetsView.Refresh(); };
        _sync.Synced += (_, result) => OnSynced(result);
        Loc.Changed += (_, _) => OnLanguageChanged();

        _stats.Updated += (_, _) => System.Windows.Application.Current?.Dispatcher.Invoke(ApplyRanking);

        ReloadPresets();
        DetectInstallations();
        _ = _stats.LoadAsync();

        // Shortcuts saved while the game was open (or before a game update) are written now.
        if (_hotkeys.Hotkeys.Count > 0) _hotkeys.Apply();
    }

    // ---- Community ranking ------------------------------------------------------------------

    /// <summary>How many of the most installed presets get the 🔥 badge.</summary>
    private const int PopularCount = 10;

    /// <summary>The ranking has data, so the "Most used" filter is offered.</summary>
    public bool HasRanking => _stats.Counts.Count > 0;

    /// <summary>Copies the community install counts onto the presets (official presets only).</summary>
    private void ApplyRanking()
    {
        var counts = _stats.Counts;
        var popular = counts.Where(c => c.Value > 0).OrderByDescending(c => c.Value).Take(PopularCount)
            .Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in Presets)
        {
            var installs = !preset.IsCustom && counts.TryGetValue(preset.FileName, out var n) ? n : 0;
            preset.Installs = installs;
            preset.IsPopular = installs > 0 && popular.Contains(preset.FileName);
        }

        OnPropertyChanged(nameof(HasRanking));
        RebuildFilterOptions();
    }

    private void OnLanguageChanged()
    {
        RebuildFilterOptions();
        if (_newPresetsCount > 0) NewPresetsBanner = Loc.Format("Inst.NewPresets", _newPresetsCount);
        RefreshReShadeStatus();
        OnPropertyChanged(nameof(UndoTooltip));
        PreviewMessage = _previewMessageKey is null ? null : Loc.Get(_previewMessageKey);
        OnPropertyChanged(nameof(SelectedPreset)); // re-reads the localized "no description" text
    }

    // ---- Presets ---------------------------------------------------------------------------

    public ObservableCollection<Preset> Presets { get; } = new();
    public ICollectionView PresetsView { get; }

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) { PresetsView.Refresh(); OnPropertyChanged(nameof(VisibleCount)); }

    // ---- Filters ---------------------------------------------------------------------------

    private const string ShowFavorites = "favorites";
    private const string ShowNew = "new";
    private const string ShowPopular = "popular";
    private const string ShowLight = "light";

    public ObservableCollection<FilterOption> ShowOptions { get; } = new();
    public ObservableCollection<FilterOption> AuthorOptions { get; } = new();
    public ObservableCollection<FilterOption> MapOptions { get; } = new();

    [ObservableProperty] private FilterOption? _selectedShow;
    [ObservableProperty] private FilterOption? _selectedAuthor;
    [ObservableProperty] private FilterOption? _selectedMap;

    partial void OnSelectedShowChanged(FilterOption? value) => FiltersChanged();
    partial void OnSelectedAuthorChanged(FilterOption? value) => FiltersChanged();
    partial void OnSelectedMapChanged(FilterOption? value) => FiltersChanged();

    private bool _rebuildingFilters;

    private void FiltersChanged()
    {
        if (_rebuildingFilters) return;

        // "Most used" also sorts by installs; the other filters keep the library order.
        var sortByInstalls = SelectedShow?.Value == ShowPopular;
        if (sortByInstalls != (PresetsView.SortDescriptions.Count > 0))
        {
            PresetsView.SortDescriptions.Clear();
            if (sortByInstalls)
                PresetsView.SortDescriptions.Add(new SortDescription(nameof(Preset.Installs), ListSortDirection.Descending));
        }

        PresetsView.Refresh();
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(ActiveFiltersText));
        OnPropertyChanged(nameof(VisibleCount));
    }

    public bool HasActiveFilters =>
        SelectedShow?.Value is not null || SelectedAuthor?.Value is not null
        || SelectedMap?.Value is not null;

    /// <summary>How many filters are on (shown on the Filters button).</summary>
    public int ActiveFilterCount =>
        (SelectedShow?.Value is null ? 0 : 1) + (SelectedAuthor?.Value is null ? 0 : 1) + (SelectedMap?.Value is null ? 0 : 1);

    /// <summary>"★ Favorites · Koda · Coldwind Farm": the filters that are on, under the search box.</summary>
    public string ActiveFiltersText => string.Join(" · ",
        new[] { SelectedShow, SelectedAuthor, SelectedMap }.Where(o => o?.Value is not null).Select(o => o!.Label));

    /// <summary>The filter panel is open.</summary>
    [ObservableProperty]
    private bool _isFilterPanelOpen;

    [RelayCommand]
    private void CloseFilterPanel() => IsFilterPanelOpen = false;

    /// <summary>Presets shown with the current search and filters.</summary>
    public int VisibleCount => PresetsView.Cast<object>().Count();

    private bool Matches(Preset p)
    {
        if (!string.IsNullOrWhiteSpace(SearchText)
            && !p.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            && !(p.Author?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
            && !(p.Map?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        return SelectedShow?.Value switch
        {
            ShowFavorites when !p.IsFavorite => false,
            ShowNew when !p.IsNew => false,
            ShowPopular when p.Installs <= 0 => false,
            ShowLight when p.PerformanceLevel != PerformanceLevel.Light => false,
            _ => (SelectedAuthor?.Value is not { } author || string.Equals(p.Author, author, StringComparison.OrdinalIgnoreCase))
                 && (SelectedMap?.Value is not { } map || string.Equals(p.Map, map, StringComparison.OrdinalIgnoreCase)),
        };
    }

    /// <summary>Fills the filter lists from the presets in the library, keeping the current choices.</summary>
    private void RebuildFilterOptions()
    {
        _rebuildingFilters = true;
        try
        {
            Fill(ShowOptions, SelectedShow, new[]
            {
                new FilterOption(null, Loc.Get("Inst.Filter.All")),
                new FilterOption(ShowFavorites, Loc.Get("Inst.Filter.Favorites")),
                new FilterOption(ShowNew, Loc.Get("Inst.Filter.New")),
                new FilterOption(ShowLight, Loc.Get("Inst.Filter.Light")),
            }.Concat(HasRanking ? new[] { new FilterOption(ShowPopular, Loc.Get("Inst.Filter.Popular")) } : Array.Empty<FilterOption>()),
                v => SelectedShow = v);

            Fill(AuthorOptions, SelectedAuthor,
                Presets.Select(p => p.Author).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase)
                    .Select(a => new FilterOption(a, a))
                    .Prepend(new FilterOption(null, Loc.Get("Inst.Filter.AllAuthors"))),
                v => SelectedAuthor = v);

            Fill(MapOptions, SelectedMap,
                Presets.Select(p => p.Map).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(m => m, StringComparer.CurrentCultureIgnoreCase)
                    .Select(m => new FilterOption(m, m))
                    .Prepend(new FilterOption(null, Loc.Get("Inst.Filter.AllMaps"))),
                v => SelectedMap = v);
        }
        finally
        {
            _rebuildingFilters = false;
        }
        FiltersChanged();
    }

    private static void Fill(ObservableCollection<FilterOption> target, FilterOption? selected,
        IEnumerable<FilterOption> options, Action<FilterOption?> select)
    {
        var keep = selected?.Value;
        target.Clear();
        foreach (var option in options) target.Add(option);
        select(target.FirstOrDefault(o => string.Equals(o.Value, keep, StringComparison.OrdinalIgnoreCase)) ?? target.FirstOrDefault());
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _rebuildingFilters = true;
        SelectedShow = ShowOptions.FirstOrDefault();
        SelectedAuthor = AuthorOptions.FirstOrDefault();
        SelectedMap = MapOptions.FirstOrDefault();
        SearchText = "";
        _rebuildingFilters = false;
        FiltersChanged();
    }

    [RelayCommand]
    private void ToggleFavorite(Preset? preset)
    {
        if (preset is null) return;
        _repository.SetFavorite(preset, !preset.IsFavorite);
        OnPropertyChanged(nameof(VisibleCount));
    }

    // ---- "New presets" banner --------------------------------------------------------------

    private int _newPresetsCount;

    [ObservableProperty]
    private string? _newPresetsBanner;

    private void OnSynced(SyncResult result)
    {
        if (result.NewPresets <= 0) return;
        _newPresetsCount = result.NewPresets;
        NewPresetsBanner = Loc.Format("Inst.NewPresets", result.NewPresets);
    }

    /// <summary>Presets with the "New" badge (arrived in the last 7 days): the "N new" button stays while there are any.</summary>
    public int NewPresetCount => Presets.Count(p => p.IsNew);

    public bool HasNewPresets => NewPresetCount > 0;

    /// <summary>Opens the "New presets" window with every preset that still has the "New" badge.</summary>
    [RelayCommand]
    private void OpenNewPresets()
    {
        var presets = Presets.Where(p => p.IsNew).OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (presets.Count == 0) return;
        DismissBanner();
        var vm = new NewPresetsViewModel(presets, _github, _settings, ShowPreset, () => ShowNewPresetsCommand.Execute(null));
        new NewPresetsWindow(vm).ShowDialog();
    }

    /// <summary>Selects a preset (clearing the filters if they hide it), e.g. from the "New presets" window.</summary>
    public void ShowPreset(Preset preset)
    {
        var target = Presets.FirstOrDefault(p => string.Equals(p.FileName, preset.FileName, StringComparison.OrdinalIgnoreCase));
        if (target is null) return;
        if (!PresetsView.Cast<Preset>().Contains(target)) ClearFilters();
        SelectedPreset = target;
    }

    /// <summary>Shows only the presets that just arrived.</summary>
    [RelayCommand]
    private void ShowNewPresets()
    {
        SelectedShow = ShowOptions.FirstOrDefault(o => o.Value == ShowNew);
        DismissBanner();
    }

    [RelayCommand]
    private void DismissBanner()
    {
        _newPresetsCount = 0;
        NewPresetsBanner = null;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(TryOnMapsCommand))]
    private Preset? _selectedPreset;

    [ObservableProperty]
    private BitmapImage? _previewImage;

    [ObservableProperty]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private string? _previewMessage;

    partial void OnSelectedPresetChanged(Preset? value) => _ = LoadPreviewAsync(value);

    private void ReloadPresets()
    {
        var selected = SelectedPreset?.FileName;
        Presets.Clear();
        foreach (var preset in _repository.Presets) Presets.Add(preset);
        ApplyRanking(); // also rebuilds the filters
        OnPropertyChanged(nameof(NewPresetCount));
        OnPropertyChanged(nameof(HasNewPresets));

        SelectedPreset = Presets.FirstOrDefault(p => p.FileName == selected) ?? Presets.FirstOrDefault();
    }

    private void SetPreviewMessage(string? key)
    {
        _previewMessageKey = key;
        PreviewMessage = key is null ? null : Loc.Get(key);
    }

    private async Task LoadPreviewAsync(Preset? preset)
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();

        PreviewImage = null;
        SetPreviewMessage(null);
        IsPreviewLoading = false;
        if (preset is null) return;

        var path = PresetRepository.GetPreviewPath(preset);
        if (path is null)
        {
            SetPreviewMessage("Inst.NoPreview");
            return;
        }

        IsPreviewLoading = true;
        try
        {
            if (!File.Exists(path))
                await _github.DownloadPreviewAsync(Path.GetFileName(path), path, cts.Token);

            var image = await Task.Run(() => ImageLoader.Load(path, decodePixelWidth: 1280), cts.Token);
            if (!cts.IsCancellationRequested) PreviewImage = image;
        }
        catch (OperationCanceledException)
        {
            // Another preset was selected meanwhile.
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                SetPreviewMessage(ex is HttpRequestException ? "Inst.PreviewOffline" : "Inst.PreviewFailed");
                _log.Warning($"Preview for {preset.DisplayName} failed: {ex.Message}");

                // A damaged image would fail forever: delete it so it's downloaded again next time
                // (only downloaded previews, never the user's own screenshots).
                if (ex is not HttpRequestException && !preset.IsCustom)
                    try { File.Delete(path); } catch (Exception) { /* in use */ }
            }
        }
        finally
        {
            if (!cts.IsCancellationRequested) IsPreviewLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSource))]
    private void OpenSource() => Shell.Open(SelectedPreset?.SourceLink);

    private bool HasSource() => SelectedPreset?.SourceLink is not null;

    [RelayCommand]
    private void ShowFullscreen()
    {
        if (SelectedPreset is null || PresetRepository.GetPreviewPath(SelectedPreset) is not { } path || !File.Exists(path))
            return;

        new FullscreenImageWindow(path, SelectedPreset.DisplayName).ShowDialog();
    }

    private bool CanTryOnMaps() => !IsBusy && SelectedPreset is not null;

    /// <summary>Opens the map picker; the preview then shows this preset on in-game screenshots.</summary>
    [RelayCommand(CanExecute = nameof(CanTryOnMaps))]
    private void TryOnMaps() =>
        new MapPickerWindow(new MapPickerViewModel(SelectedPreset!, SelectedInstallation, _maps, _mapPreview, _log)).ShowDialog();

    private bool CanSync() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncPresetsAsync()
    {
        IsBusy = true;
        StatusText = Loc.Get("Inst.Status.Syncing");
        try
        {
            var result = await _sync.SyncAsync();
            Dialogs.Info(result.ChangedPresets > 0
                ? Loc.Format("Inst.Sync.New", result.NewPresets, result.UpdatedPresets)
                : Loc.Get("Inst.Sync.UpToDate"));
        }
        catch (Exception ex)
        {
            _log.Error($"Preset update failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Inst.Sync.Failed", ex.Message));
        }
        finally
        {
            IsBusy = false;
            StatusText = null;
        }
    }

    [RelayCommand]
    private void OpenPresetsFolder() => Shell.Open(AppPaths.PresetsDirectory);

    // ---- Game installation -----------------------------------------------------------------

    public ObservableCollection<GameInstallation> Installations { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DestinationFolder))]
    [NotifyPropertyChangedFor(nameof(IsUsingGameFolder))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchGameCommand))]
    private GameInstallation? _selectedInstallation;

    [ObservableProperty]
    private string _reShadeStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    private bool _isReShadeInstalled;

    /// <summary>The newest backup of this installation: what "Undo" puts back.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyPropertyChangedFor(nameof(UndoTooltip))]
    private BackupEntry? _latestBackup;

    public string UndoTooltip => LatestBackup is { } b
        ? Loc.Format("Inst.UndoTip", b.Created.ToString("g"), Loc.Get($"Backup.Reason.{b.Reason}"))
        : Loc.Get("Inst.UndoNone");

    [ObservableProperty]
    private string? _currentPresetName;

    partial void OnSelectedInstallationChanged(GameInstallation? value)
    {
        if (value is not null)
        {
            _settings.Current.LastGamePath = value.RootPath;
            _settings.Save();
        }
        _hotkeys.SetTarget(value, DestinationFolder);
        RefreshReShadeStatus();
    }

    [RelayCommand]
    private void DetectInstallations()
    {
        var found = _locator.FindInstallations();

        Installations.Clear();
        foreach (var g in found) Installations.Add(g);

        SelectedInstallation =
            Installations.FirstOrDefault(g => string.Equals(g.RootPath, _settings.Current.LastGamePath, StringComparison.OrdinalIgnoreCase))
            ?? Installations.FirstOrDefault();

        RefreshReShadeStatus();
    }

    [RelayCommand]
    private void BrowseGameFolder()
    {
        var folder = Dialogs.PickFolder(Loc.Get("Inst.PickGameTitle"));
        if (folder is null) return;

        var game = GameLocator.FromManualFolder(folder);
        if (game is null)
        {
            Dialogs.Warning(Loc.Get("Inst.NotDbdFolder"));
            return;
        }

        var existing = Installations.FirstOrDefault(g => string.Equals(g.RootPath, game.RootPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null) Installations.Add(game);
        SelectedInstallation = existing ?? game;
        _log.Info($"Using installation picked manually: {game.DisplayName}");
    }

    private void RefreshReShadeStatus()
    {
        LatestBackup = SelectedInstallation is null ? null : _backups.Latest(SelectedInstallation);
        if (SelectedInstallation is null)
        {
            IsReShadeInstalled = false;
            ReShadeStatus = Loc.Get("Inst.NoGame");
            CurrentPresetName = null;
            HealthMessage = null;
            return;
        }

        IsReShadeInstalled = _reshade.IsInstalled(SelectedInstallation);
        var version = ReShadeService.GetInstalledVersion(SelectedInstallation.BinariesDirectory);
        ReShadeStatus = !IsReShadeInstalled ? Loc.Get("Inst.ReShadeMissing")
            : version is null ? Loc.Get("Inst.ReShadeOk")
            : Loc.Format("Inst.ReShadeOkVersion", version);

        var current = _reshade.GetCurrentPresetPath(SelectedInstallation);
        CurrentPresetName = string.IsNullOrWhiteSpace(current) ? null : Path.GetFileNameWithoutExtension(current);
        CheckGameHealth();
    }

    // ---- After a game update ------------------------------------------------------------------

    /// <summary>"Dead by Daylight was updated and ReShade is no longer installed…" (null = all fine).</summary>
    [ObservableProperty]
    private string? _healthMessage;

    /// <summary>Size and date of the game exe: they change when the game updates.</summary>
    private static string? GameStamp(GameInstallation game)
    {
        try
        {
            return game.GameExecutable is { } exe && new FileInfo(exe) is { Exists: true } info
                ? $"{info.Length}|{info.LastWriteTimeUtc.Ticks}"
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private InstallRecord? RecordFor(GameInstallation game) =>
        _settings.Current.Installs.FirstOrDefault(r => string.Equals(r.Key, game.RootPath, StringComparison.OrdinalIgnoreCase)).Value;

    private void RecordInstall(GameInstallation game, string presetFileName, string presetPath)
    {
        ForgetInstall(game);
        _settings.Current.Installs[game.RootPath] = new InstallRecord
        {
            PresetFileName = presetFileName, PresetPath = presetPath, GameStamp = GameStamp(game),
        };
        _settings.Save();
    }

    /// <summary>
    /// Compares what the app installed with what's there now. Game updates (or Steam's "verify files")
    /// can remove ReShade's DLL, its settings, the effects or the preset: then a banner offers to repair it.
    /// </summary>
    private void CheckGameHealth()
    {
        var previous = HealthMessage;
        HealthMessage = null;
        if (SelectedInstallation is not { } game || RecordFor(game) is not { } record) return;
        if (ReShadeService.IsGameRunning(game)) return;

        var stamp = GameStamp(game);
        var updated = stamp is not null && record.GameStamp is not null && stamp != record.GameStamp;

        var problems = new List<string>();
        if (!_reshade.IsInstalled(game)) problems.Add(Loc.Get("Health.NoReShade"));
        else
        {
            if (!File.Exists(game.ReShadeIniPath)) problems.Add(Loc.Get("Health.NoIni"));
            if (!File.Exists(Path.Combine(game.BinariesDirectory, "reshade-shaders", "Shaders", "ReShade.fxh")))
                problems.Add(Loc.Get("Health.NoEffects"));
        }
        if (!string.IsNullOrEmpty(record.PresetPath) && !File.Exists(record.PresetPath)) problems.Add(Loc.Get("Health.NoPreset"));

        if (problems.Count == 0)
        {
            if (updated)
            {
                _log.Success("Dead by Daylight was updated: ReShade and the preset are still in place.");
                record.GameStamp = stamp;
                _settings.Save();
            }
            return;
        }

        var message = Loc.Format(updated ? "Health.Updated" : "Health.Broken", string.Join(", ", problems));
        if (message != previous)
            _log.Warning($"Game check: {string.Join("; ", problems)}{(updated ? " (after a game update)" : "")}");
        HealthMessage = message;
    }

    /// <summary>Installs again the last preset (with ReShade and the effects) to fix what the update removed.</summary>
    [RelayCommand]
    private async Task RepairAsync()
    {
        if (SelectedInstallation is not { } game || RecordFor(game) is not { } record) return;
        var preset = Presets.FirstOrDefault(p => string.Equals(p.FileName, record.PresetFileName, StringComparison.OrdinalIgnoreCase));
        if (preset is not null)
        {
            if (!PresetsView.Cast<Preset>().Contains(preset)) ClearFilters();
            SelectedPreset = preset;
        }
        if (SelectedPreset is null) return;
        if (InstallCommand.CanExecute(null)) await InstallCommand.ExecuteAsync(null);
    }

    /// <summary>"Ignore": forget the last install, so the warning doesn't come back.</summary>
    [RelayCommand]
    private void DismissHealth()
    {
        if (SelectedInstallation is { } game) ForgetInstall(game);
        HealthMessage = null;
    }

    private void ForgetInstall(GameInstallation game)
    {
        foreach (var key in _settings.Current.Installs.Keys.Where(k => string.Equals(k, game.RootPath, StringComparison.OrdinalIgnoreCase)).ToList())
            _settings.Current.Installs.Remove(key);
        _settings.Save();
    }

    // ---- Destination folder ----------------------------------------------------------------

    /// <summary>Where the preset .ini is copied. Defaults to the game's binaries folder.</summary>
    public string? DestinationFolder =>
        _settings.Current.CustomPresetFolder ?? SelectedInstallation?.BinariesDirectory;

    public bool IsUsingGameFolder => _settings.Current.CustomPresetFolder is null;

    [RelayCommand]
    private void BrowseDestination()
    {
        var folder = Dialogs.PickFolder(Loc.Get("Inst.PickDestTitle"), DestinationFolder);
        if (folder is null) return;

        _settings.Current.CustomPresetFolder = folder;
        _settings.Save();
        DestinationChanged();
    }

    [RelayCommand]
    private void UseGameFolder()
    {
        _settings.Current.CustomPresetFolder = null;
        _settings.Save();
        DestinationChanged();
    }

    private void DestinationChanged()
    {
        _hotkeys.SetTarget(SelectedInstallation, DestinationFolder);
        OnPropertyChanged(nameof(DestinationFolder));
        OnPropertyChanged(nameof(IsUsingGameFolder));
        InstallCommand.NotifyCanExecuteChanged();
    }

    // ---- Install ---------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncPresetsCommand))]
    [NotifyCanExecuteChangedFor(nameof(TryOnMapsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _statusText;

    private bool CanInstall() =>
        !IsBusy && SelectedPreset is not null && SelectedInstallation is not null
        && !string.IsNullOrWhiteSpace(DestinationFolder);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var preset = SelectedPreset!;
        var game = SelectedInstallation!;
        var destinationFolder = DestinationFolder!;

        // With the game open ReShade's files are in use, and ReShade would overwrite ReShade.ini on exit.
        if (ReShadeService.IsGameRunning(game))
        {
            Dialogs.Warning(Loc.Get("Inst.CloseGame"));
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        _log.Info($"Installing preset {preset.DisplayName} on {game.DisplayName}");

        try
        {
            TryBackup(game, "install");

            if (!_reshade.IsInstalled(game))
            {
                // ReShade's setup would replace another mod's dxgi.dll (and break that mod) without asking.
                if (ReShadeService.ForeignDxgi(game) is { } foreign)
                {
                    _log.Error($"{foreign} belongs to another program: ReShade was not installed.");
                    Dialogs.Error(Loc.Format("Inst.ForeignDxgi", foreign));
                    return;
                }

                StatusText = Loc.Get("Inst.Status.InstallingReShade");
                var progress = new System.Progress<double>(p => ProgressValue = 5 + p * 35);
                await _reshade.InstallAsync(game, progress);
            }
            else
            {
                await OfferReShadeUpdateAsync(game);
            }
            ProgressValue = 40;

            // ReShade's unattended setup installs no effects: download the ones this preset uses
            // (only those missing, so with everything in place this is just a quick check).
            StatusText = Loc.Get("Inst.Status.CheckingEffects");
            var effectsProgress = new System.Progress<ShaderProgress>(p =>
            {
                if (p.PackageName.Length > 0) StatusText = Loc.Format("Inst.Status.Effects", p.PackageName);
                ProgressValue = 40 + 45 * (p.Index + p.Fraction) / Math.Max(1, p.Count);
            });
            // Effects for the whole library, so changing preset from ReShade's overlay in game works too.
            var presetFiles = _repository.Presets.Select(p => p.FilePath).Append(preset.FilePath).Distinct();
            var missingEffects = await _shaders.EnsureForPresetsAsync(
                game.BinariesDirectory, game.ReShadeIniPath, presetFiles, effectsProgress);
            var missingHere = missingEffects.Intersect(ShaderService.EffectFilesOf(preset.FilePath), StringComparer.OrdinalIgnoreCase).ToList();
            ProgressValue = 85;

            StatusText = Loc.Get("Inst.Status.Copying");
            Directory.CreateDirectory(destinationFolder);
            var target = Path.Combine(destinationFolder, preset.FileName);
            await Task.Run(() => File.Copy(preset.FilePath, target, overwrite: true));
            _log.Info($"Preset copied to {target}");
            ProgressValue = 92;

            StatusText = Loc.Get("Inst.Status.Configuring");
            _reshade.SetPresetPath(game, target);
            if (_hotkeys.Hotkeys.Count > 0) _hotkeys.Apply();
            RecordInstall(game, preset.FileName, target);
            ProgressValue = 100;

            _log.Success($"Preset {preset.DisplayName} installed!");
            if (!preset.IsCustom) _ = _stats.ReportInstallAsync(preset.FileName);
            var launchNow = Dialogs.Confirm(Loc.Format("Inst.DoneLaunch", preset.DisplayName));
            if (missingHere.Count > 0)
                Dialogs.Warning(Loc.Format("Inst.EffectsMissing", string.Join(", ", missingHere)));
            if (launchNow) LaunchGame();
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Error(ex.Message);
            Dialogs.Error(Loc.Format("Inst.AccessDenied", ex.Message));
        }
        catch (HttpRequestException ex)
        {
            _log.Error($"Installation failed, no connection: {ex.Message}");
            Dialogs.Error(Loc.Get("Inst.NoInternet"));
        }
        catch (Exception ex)
        {
            _log.Error($"Installation failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Inst.Failed", ex.Message));
        }
        finally
        {
            RefreshReShadeStatus();
            IsBusy = false;
            StatusText = null;
            ProgressValue = 0;
        }
    }

    /// <summary>If reshade.me has a newer ReShade than the game's, asks and updates it (settings are kept).</summary>
    private async Task OfferReShadeUpdateAsync(GameInstallation game)
    {
        StatusText = Loc.Get("Inst.Status.CheckingReShade");
        (Version Installed, Version Latest, string Url)? update;
        try
        {
            update = await _reshade.CheckForUpdateAsync(game.BinariesDirectory);
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not check for a ReShade update: {ex.Message}");
            return; // offline or reshade.me changed: keep the installed version
        }

        if (update is not { } u)
        {
            _log.Info("ReShade is installed and up to date.");
            return;
        }

        _log.Info($"ReShade {u.Latest} is available (installed: {u.Installed}).");
        if (!Dialogs.Confirm(Loc.Format("Inst.ReShadeUpdate", u.Installed, u.Latest))) return;

        StatusText = Loc.Format("Inst.Status.UpdatingReShade", u.Latest);
        await _reshade.UpdateAsync(game.GameExecutable!, u.Url, new System.Progress<double>(p => ProgressValue = 5 + p * 35));
    }

    // ---- Backup, undo, uninstall ------------------------------------------------------------

    /// <summary>A failed backup must not stop the install: it's logged and the install goes on.</summary>
    private void TryBackup(GameInstallation game, string reason)
    {
        try
        {
            _backups.Create(game, reason);
        }
        catch (Exception ex)
        {
            _log.Warning($"Backup before {reason} failed: {ex.Message}");
        }
    }

    private bool CanUndo() => !IsBusy && LatestBackup is not null;

    /// <summary>Puts back ReShade.ini, the preset and ReShade as they were before the last change.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        var game = SelectedInstallation!;
        var backup = LatestBackup!;
        if (ReShadeService.IsGameRunning(game))
        {
            Dialogs.Warning(Loc.Get("Inst.CloseGame"));
            return;
        }

        var preset = backup.PresetName ?? Loc.Get("Inst.UndoNoPreset");
        if (!Dialogs.Confirm(Loc.Format("Inst.UndoConfirm", backup.Created.ToString("g"), preset))) return;

        IsBusy = true;
        ProgressValue = 0;
        StatusText = Loc.Get("Inst.Status.Restoring");
        try
        {
            await Task.Run(() => _backups.Restore(backup));
            ForgetInstall(game); // the folder is now as the backup left it, on purpose

            // The effects aren't part of the backup: make sure the restored preset has them.
            var presetPath = BackupService.ResolvePresetPath(game, _reshade.GetCurrentPresetPath(game));
            if (_reshade.IsInstalled(game) && presetPath is not null && File.Exists(presetPath))
            {
                StatusText = Loc.Get("Inst.Status.CheckingEffects");
                await _shaders.EnsureForPresetAsync(game.BinariesDirectory, game.ReShadeIniPath, presetPath,
                    new System.Progress<ShaderProgress>(p =>
                    {
                        if (p.PackageName.Length > 0) StatusText = Loc.Format("Inst.Status.Effects", p.PackageName);
                        ProgressValue = 100.0 * (p.Index + p.Fraction) / Math.Max(1, p.Count);
                    }));
            }
            Dialogs.Info(Loc.Get("Inst.UndoDone"));
        }
        catch (Exception ex)
        {
            _log.Error($"Restoring the backup failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Inst.UndoFailed", ex.Message));
        }
        finally
        {
            RefreshReShadeStatus();
            IsBusy = false;
            StatusText = null;
            ProgressValue = 0;
        }
    }

    private bool CanUninstall() => !IsBusy && IsReShadeInstalled;

    /// <summary>Removes ReShade, its effects and settings from the game (a backup is taken first).</summary>
    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private void Uninstall()
    {
        var game = SelectedInstallation!;
        if (ReShadeService.IsGameRunning(game))
        {
            Dialogs.Warning(Loc.Get("Inst.CloseGame"));
            return;
        }
        if (!Dialogs.Confirm(Loc.Format("Inst.UninstallConfirm", game.DisplayName))) return;

        try
        {
            TryBackup(game, "uninstall");
            _reshade.Uninstall(game, _repository.Presets.Select(p => p.FileName));
            ForgetInstall(game); // removed on purpose: no "repair" warning
            Dialogs.Info(Loc.Get("Inst.UninstallDone"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Uninstall failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Inst.UninstallFailed", ex.Message));
        }
        finally
        {
            RefreshReShadeStatus();
        }
    }

    // ---- Launch -----------------------------------------------------------------------------

    private bool CanLaunchGame() => !IsBusy && SelectedInstallation is not null;

    /// <summary>Starts the game through Steam/Epic, which open first if they're closed.</summary>
    [RelayCommand(CanExecute = nameof(CanLaunchGame))]
    private void LaunchGame()
    {
        var game = SelectedInstallation!;
        if (ReShadeService.IsGameRunning(game))
        {
            Dialogs.Info(Loc.Get("Inst.GameRunning"));
            return;
        }

        try
        {
            _launcher.Launch(game);
        }
        catch (Exception ex)
        {
            _log.Error($"Could not start the game: {ex.Message}");
            Dialogs.Error(Loc.Format("Inst.LaunchFailed", ex.Message));
        }
    }
}
