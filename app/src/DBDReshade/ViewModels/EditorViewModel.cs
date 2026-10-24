using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Models;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>Starting point for a new preset: empty, or a copy of an existing one.</summary>
public sealed record EditorBase(Preset? Preset, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Preset editor. ReShade is the editor: the preview window opens on the work-in-progress file with
/// editing enabled, and ReShade saves every change into it. This page handles the rest:
/// 1. name + starting point, 2. screenshots, 3. open the editor, 4. save to the library.
/// Drafts live in %LOCALAPPDATA%\DBDReshade\Editor, so work survives closing the app.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly PresetRepository _repository;
    private readonly MapService _maps;
    private readonly MapPreviewService _preview;
    private readonly Func<GameInstallation?> _game;
    private readonly LogService _log;
    private bool _mapsLoaded;
    private bool _screenshotPickedByUser;

    public EditorViewModel(PresetRepository repository, MapService maps, MapPreviewService preview,
        Func<GameInstallation?> game, LogService log)
    {
        _repository = repository;
        _maps = maps;
        _preview = preview;
        _game = game;
        _log = log;

        _repository.Changed += (_, _) => ReloadBases();
        Loc.Changed += (_, _) => { ReloadBases(); RefreshUserEntry(select: false); };
        ReloadBases();
    }

    /// <summary>Called when the page is opened: loads the realm list the first time.</summary>
    public async Task ActivateAsync()
    {
        if (_mapsLoaded) return;
        _mapsLoaded = true;
        foreach (var map in await _maps.GetMapsAsync()) Maps.Add(map);
        SelectedMap = Maps.FirstOrDefault(m => m.IsDownloaded)
                      ?? (MapService.HasUserScreenshots ? Maps.FirstOrDefault(m => m.IsUser) : Maps.FirstOrDefault(m => !m.IsUser))
                      ?? Maps.FirstOrDefault();
    }

    // ---- 1. Name and starting point --------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(HasDraft), nameof(DraftPath))]
    [NotifyCanExecuteChangedFor(nameof(OpenEditorCommand), nameof(SaveCommand), nameof(DiscardCommand))]
    private string _name = "";

    /// <summary>Error text for the name field, or null when it's valid (or still empty).</summary>
    public string? NameError =>
        !string.IsNullOrWhiteSpace(Name) && !IsValidName(Name) ? Loc.Get("Edit.InvalidName") : null;

    public ObservableCollection<EditorBase> Bases { get; } = new();

    [ObservableProperty]
    private EditorBase? _selectedBase;

    private void ReloadBases()
    {
        var selected = SelectedBase?.Preset?.FileName;
        Bases.Clear();
        Bases.Add(new EditorBase(null, Loc.Get("Edit.Empty")));
        foreach (var preset in _repository.Presets) Bases.Add(new EditorBase(preset, preset.DisplayName));
        SelectedBase = Bases.FirstOrDefault(b => b.Preset?.FileName == selected && selected is not null) ?? Bases[0];
    }

    private static bool IsValidName(string name) =>
        name.Trim().Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Trim().EndsWith('.');

    private string FileName => Name.Trim() + ".ini";

    /// <summary>The work-in-progress preset file for the current name.</summary>
    public string? DraftPath => IsValidName(Name) ? Path.Combine(AppPaths.EditorDirectory, FileName) : null;

    /// <summary>A draft with this name already exists: "Open" continues it instead of starting over.</summary>
    public bool HasDraft => DraftPath is { } path && File.Exists(path);

    // ---- 2. Screenshots ---------------------------------------------------------------------

    public ObservableCollection<MapInfo> Maps { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenEditorCommand))]
    private MapInfo? _selectedMap;

    [RelayCommand]
    private void AddScreenshots()
    {
        var files = Dialogs.PickFiles(Loc.Get("Map.PickScreenshots"),
            $"{Loc.Get("Man.ImageFilter")} (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
        if (files.Count == 0) return;

        try
        {
            var added = _maps.AddUserScreenshots(files);
            RefreshUserEntry(select: true);
            Dialogs.Info(Loc.Format("Map.ScreenshotsAdded", added));
        }
        catch (Exception ex)
        {
            _log.Error($"Adding screenshots failed: {ex.Message}");
            Dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    private void OpenMyScreenshots()
    {
        Shell.Open(AppPaths.UserScreenshotsDirectory);
        RefreshUserEntry(select: false);
    }

    private void RefreshUserEntry(bool select)
    {
        if (!_mapsLoaded) return;
        var wasSelected = SelectedMap?.IsUser == true;
        var index = Maps.ToList().FindIndex(m => m.IsUser);
        var entry = MapService.UserEntry;
        if (index >= 0) Maps[index] = entry; else Maps.Insert(0, entry);
        if (select || wasSelected) SelectedMap = entry;
    }

    // ---- 3. Edit ----------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenEditorCommand), nameof(SaveCommand), nameof(DiscardCommand))]
    private bool _isBusy;

    /// <summary>The editor window is open.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenEditorCommand), nameof(SaveCommand), nameof(DiscardCommand))]
    private bool _isEditing;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _statusText;

    private bool CanOpenEditor() => !IsBusy && !IsEditing && IsValidName(Name) && SelectedMap is not null;

    [RelayCommand(CanExecute = nameof(CanOpenEditor))]
    private async Task OpenEditorAsync()
    {
        var map = SelectedMap!;
        if (map.IsUser && !MapService.HasUserScreenshots)
        {
            Dialogs.Info(Loc.Get("Map.NoMyScreenshots"));
            return;
        }

        var draft = DraftPath!;
        IsBusy = true;
        ProgressValue = 0;
        try
        {
            // New draft: start empty or from a copy of the chosen preset. An existing draft is continued.
            Directory.CreateDirectory(AppPaths.EditorDirectory);
            if (!File.Exists(draft))
            {
                if (SelectedBase?.Preset is { } basePreset) File.Copy(basePreset.FilePath, draft);
                else await File.WriteAllTextAsync(draft, "");
                _log.Info($"Editor: new draft {FileName}{(SelectedBase?.Preset is { } b ? $" based on {b.FileName}" : "")}");
                OnPropertyChanged(nameof(HasDraft));
            }

            StatusText = Loc.Get("Map.Status.Downloading");
            var folder = await _maps.EnsureDownloadedAsync(map, new System.Progress<double>(p => ProgressValue = p * 60));

            StatusText = Loc.Get(MapPreviewService.IsReShadeInstalled ? "Map.Status.Starting" : "Map.Status.InstallingReShade");
            var shadersFound = await _preview.PrepareEditorAsync(draft, _game(),
                new System.Progress<double>(p => ProgressValue = 60 + p * 35));
            ProgressValue = 100;
            if (!shadersFound) Dialogs.Warning(Loc.Get("Map.NoShaders"));

            var started = DateTime.Now;
            using var process = _preview.Launch(folder, $"{Name.Trim()} · {map.Name}");

            IsBusy = false;
            IsEditing = true;
            StatusText = null;
            await process.WaitForExitAsync();
            _log.Info($"Editor closed: {FileName}");

            PickLatestScreenshot(started);
            EditedOnce = true;
        }
        catch (Exception ex)
        {
            _log.Error($"Preset editor failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Map.Failed", ex.Message));
        }
        finally
        {
            IsBusy = false;
            IsEditing = false;
            StatusText = null;
            ProgressValue = 0;
        }
    }

    /// <summary>Uses the last screenshot taken in this editing session as the preview, unless one was chosen by hand.</summary>
    private void PickLatestScreenshot(DateTime since)
    {
        if (_screenshotPickedByUser || !Directory.Exists(AppPaths.EditorScreenshotsDirectory)) return;

        var latest = new DirectoryInfo(AppPaths.EditorScreenshotsDirectory)
            .EnumerateFiles()
            .Where(f => f.Extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp")
            .Where(f => f.LastWriteTime >= since)
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        if (latest is not null) SetScreenshot(latest.FullName, byUser: false);
    }

    private bool CanDiscard() => !IsBusy && !IsEditing && HasDraft;

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard()
    {
        if (!Dialogs.Confirm(Loc.Format("Edit.ConfirmDiscard", Name.Trim()))) return;
        try
        {
            File.Delete(DraftPath!);
            _log.Info($"Editor: draft {FileName} discarded.");
        }
        catch (IOException ex)
        {
            Dialogs.Error(ex.Message);
        }
        EditedOnce = false;
        SetScreenshot(null, byUser: false);
        OnPropertyChanged(nameof(HasDraft));
        SaveCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }

    // ---- 4. Save ----------------------------------------------------------------------------

    /// <summary>The editor was used at least once in this session (shows the save step prominently).</summary>
    [ObservableProperty]
    private bool _editedOnce;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _description = "";

    [ObservableProperty]
    private string _source = "";

    [ObservableProperty]
    private string? _screenshotPath;

    [ObservableProperty]
    private BitmapImage? _screenshotPreview;

    [RelayCommand]
    private void ChooseScreenshot()
    {
        var path = Dialogs.PickFile(Loc.Get("Man.PickImage"),
            $"{Loc.Get("Man.ImageFilter")} (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
        if (path is not null) SetScreenshot(path, byUser: true);
    }

    [RelayCommand]
    private void ClearScreenshot() => SetScreenshot(null, byUser: true);

    private void SetScreenshot(string? path, bool byUser)
    {
        _screenshotPickedByUser = byUser && path is not null;
        ScreenshotPath = path;
        try
        {
            ScreenshotPreview = path is null ? null : ImageLoader.Load(path, decodePixelWidth: 640);
        }
        catch (Exception ex)
        {
            ScreenshotPreview = null;
            _log.Warning($"Cannot read image: {ex.Message}");
        }
    }

    private bool CanSave() => !IsBusy && !IsEditing && HasDraft && !string.IsNullOrWhiteSpace(Description);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(Source)
            && !(Uri.TryCreate(Source.Trim(), UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            Dialogs.Warning(Loc.Get("Man.InvalidSource"));
            return;
        }

        var fileName = FileName;
        if (_repository.IsOfficial(fileName) && !_repository.IsCustom(fileName))
        {
            switch (Dialogs.YesNoCancel(Loc.Format("Man.ConflictOfficial", Name.Trim())))
            {
                case true: fileName = _repository.GetFreeCopyName(fileName, Loc.Get("Common.Custom")); break;
                case false: break;
                default: return;
            }
        }
        else if (_repository.Exists(fileName) && !Dialogs.Confirm(Loc.Format("Man.Exists", fileName)))
        {
            return;
        }

        try
        {
            _repository.AddCustom(DraftPath!, fileName, Description, Source, ScreenshotPath);
            _log.Success($"Editor: preset {fileName} saved to the library.");
            Dialogs.Info(Loc.Format("Edit.Saved", Path.GetFileNameWithoutExtension(fileName)));
        }
        catch (Exception ex)
        {
            _log.Error($"Saving preset failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Man.SaveFailed", ex.Message));
        }
    }

    [RelayCommand]
    private void OpenScreenshotsFolder() => Shell.Open(AppPaths.EditorScreenshotsDirectory);
}
