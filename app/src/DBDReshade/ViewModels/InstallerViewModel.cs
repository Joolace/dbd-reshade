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
    private readonly GitHubService _github;
    private readonly SettingsService _settings;
    private readonly LogService _log;
    private CancellationTokenSource? _previewCts;
    private string? _previewMessageKey;

    public InstallerViewModel(
        PresetRepository repository, PresetSyncService sync, GameLocator locator, ReShadeService reshade,
        GitHubService github, SettingsService settings, LogService log)
    {
        _repository = repository;
        _sync = sync;
        _locator = locator;
        _reshade = reshade;
        _github = github;
        _settings = settings;
        _log = log;

        PresetsView = CollectionViewSource.GetDefaultView(Presets);
        PresetsView.Filter = o => o is Preset p
            && (string.IsNullOrWhiteSpace(SearchText) || p.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        _repository.Changed += (_, _) => ReloadPresets();
        Loc.Changed += (_, _) => OnLanguageChanged();

        ReloadPresets();
        DetectInstallations();
    }

    private void OnLanguageChanged()
    {
        RefreshReShadeStatus();
        PreviewMessage = _previewMessageKey is null ? null : Loc.Get(_previewMessageKey);
        OnPropertyChanged(nameof(SelectedPreset)); // re-reads the localized "no description" text
    }

    // ---- Presets ---------------------------------------------------------------------------

    public ObservableCollection<Preset> Presets { get; } = new();
    public ICollectionView PresetsView { get; }

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => PresetsView.Refresh();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSourceCommand))]
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
                SetPreviewMessage("Inst.PreviewFailed");
                _log.Warning($"Preview for {preset.DisplayName} failed: {ex.Message}");
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
    private GameInstallation? _selectedInstallation;

    [ObservableProperty]
    private string _reShadeStatus = "";

    [ObservableProperty]
    private bool _isReShadeInstalled;

    [ObservableProperty]
    private string? _currentPresetName;

    partial void OnSelectedInstallationChanged(GameInstallation? value)
    {
        if (value is not null)
        {
            _settings.Current.LastGamePath = value.RootPath;
            _settings.Save();
        }
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
        if (SelectedInstallation is null)
        {
            IsReShadeInstalled = false;
            ReShadeStatus = Loc.Get("Inst.NoGame");
            CurrentPresetName = null;
            return;
        }

        IsReShadeInstalled = _reshade.IsInstalled(SelectedInstallation);
        ReShadeStatus = Loc.Get(IsReShadeInstalled ? "Inst.ReShadeOk" : "Inst.ReShadeMissing");

        var current = _reshade.GetCurrentPresetPath(SelectedInstallation);
        CurrentPresetName = string.IsNullOrWhiteSpace(current) ? null : Path.GetFileNameWithoutExtension(current);
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
        OnPropertyChanged(nameof(DestinationFolder));
        OnPropertyChanged(nameof(IsUsingGameFolder));
        InstallCommand.NotifyCanExecuteChanged();
    }

    // ---- Install ---------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncPresetsCommand))]
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

        IsBusy = true;
        ProgressValue = 0;
        _log.Info($"Installing preset {preset.DisplayName} on {game.DisplayName}");

        try
        {
            if (!_reshade.IsInstalled(game))
            {
                StatusText = Loc.Get("Inst.Status.InstallingReShade");
                var progress = new System.Progress<double>(p => ProgressValue = 5 + p * 65);
                await _reshade.InstallAsync(game, progress);
            }
            else
            {
                _log.Info("ReShade is already installed: only the preset will be changed.");
            }
            ProgressValue = 75;

            StatusText = Loc.Get("Inst.Status.Copying");
            Directory.CreateDirectory(destinationFolder);
            var target = Path.Combine(destinationFolder, preset.FileName);
            await Task.Run(() => File.Copy(preset.FilePath, target, overwrite: true));
            _log.Info($"Preset copied to {target}");
            ProgressValue = 90;

            StatusText = Loc.Get("Inst.Status.Configuring");
            _reshade.SetPresetPath(game, target);
            ProgressValue = 100;

            _log.Success($"Preset {preset.DisplayName} installed!");
            Dialogs.Info(Loc.Format("Inst.Done", preset.DisplayName));
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Error(ex.Message);
            Dialogs.Error(Loc.Format("Inst.AccessDenied", ex.Message));
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
}
