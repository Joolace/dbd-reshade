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
            SelectedMap = Maps.FirstOrDefault(m => m.IsDownloaded) ?? Maps.FirstOrDefault();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanStart() => !IsBusy && SelectedMap is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var map = SelectedMap!;
        IsBusy = true;
        ProgressValue = 0;
        try
        {
            StatusText = Loc.Get("Map.Status.Downloading");
            var folder = await _maps.EnsureDownloadedAsync(map.Name, new System.Progress<double>(p => ProgressValue = p * 60));

            StatusText = Loc.Get(MapPreviewService.IsReShadeInstalled ? "Map.Status.Starting" : "Map.Status.InstallingReShade");
            var shadersFound = await _preview.PrepareAsync(_preset, _game,
                new System.Progress<double>(p => ProgressValue = 60 + p * 35));
            ProgressValue = 100;

            StatusText = Loc.Get("Map.Status.Starting");
            _preview.Launch(folder, map.Name, Loc.Get("Map.ViewerHint"));

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
