using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Models;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>"Try on maps": pick a map, then the preset opens on its screenshots in the D3D preview.</summary>
public sealed partial class MapPickerViewModel : ObservableObject
{
    private readonly Preset _preset;
    private readonly GameInstallation? _game;
    private readonly MapService _maps;
    private readonly MapPreviewService _preview;
    private readonly LogService _log;

    public MapPickerViewModel(Preset preset, GameInstallation? game, MapService maps, MapPreviewService preview, LogService log)
    {
        _preset = preset;
        _game = game;
        _maps = maps;
        _preview = preview;
        _log = log;
    }

    /// <summary>Asks the window to close (after the preview started).</summary>
    public event EventHandler? RequestClose;

    public string PresetName => _preset.DisplayName;

    public ObservableCollection<MapInfo> Maps { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private MapInfo? _selectedMap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading = true;

    public bool IsEmpty => !IsLoading && Maps.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _statusText;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            foreach (var map in await _maps.GetMapsAsync()) Maps.Add(map);
            // A realm already on disk, else your own screenshots if you have some, else the first realm.
            SelectedMap = Maps.FirstOrDefault(m => m.IsDownloaded)
                          ?? (MapService.HasUserScreenshots ? Maps.FirstOrDefault(m => m.IsUser) : Maps.FirstOrDefault(m => !m.IsUser))
                          ?? Maps.FirstOrDefault();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanStart() => !IsBusy && SelectedMap is not null;

    /// <summary>Adds the user's own images to "My screenshots" and selects that entry.</summary>
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
        RefreshUserEntry(select: false); // the user may add or delete files there
    }

    private void RefreshUserEntry(bool select)
    {
        var wasSelected = SelectedMap?.IsUser == true;
        var index = Maps.ToList().FindIndex(m => m.IsUser);
        var entry = MapService.UserEntry;
        if (index >= 0) Maps[index] = entry; else Maps.Insert(0, entry);
        if (select || wasSelected) SelectedMap = entry;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var map = SelectedMap!;
        if (map.IsUser && !MapService.HasUserScreenshots)
        {
            Dialogs.Info(Loc.Get("Map.NoMyScreenshots"));
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        try
        {
            StatusText = Loc.Get("Map.Status.Downloading");
            var folder = await _maps.EnsureDownloadedAsync(map, new System.Progress<double>(p => ProgressValue = p * 60));

            StatusText = Loc.Get(MapPreviewService.IsReShadeInstalled ? "Map.Status.Starting" : "Map.Status.InstallingReShade");
            var shadersFound = await _preview.PrepareAsync(_preset, _game,
                new System.Progress<double>(p => ProgressValue = 60 + p * 35));
            ProgressValue = 100;

            StatusText = Loc.Get("Map.Status.Starting");
            _preview.Launch(folder, map.Name);

            if (!shadersFound) Dialogs.Warning(Loc.Get("Map.NoShaders"));
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log.Error($"Map preview failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Map.Failed", ex.Message));
        }
        finally
        {
            IsBusy = false;
            StatusText = null;
            ProgressValue = 0;
        }
    }
}
