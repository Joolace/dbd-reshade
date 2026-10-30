using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Models;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>
/// Full-screen preview: browse the presets (in the order and with the filters of the list) with the arrows,
/// install one, star it or look at the log, without leaving the preview.
/// </summary>
public sealed partial class PresetViewerViewModel : ObservableObject
{
    private readonly GitHubService _github;
    private readonly LogService _log;
    private readonly List<Preset> _presets;
    private CancellationTokenSource? _loadCts;

    public PresetViewerViewModel(InstallerViewModel installer, GitHubService github, LogService log)
    {
        Installer = installer;
        _github = github;
        _log = log;

        // The list as the user sees it now (search, filters and sort included).
        _presets = installer.PresetsView.Cast<Preset>().ToList();
        if (_presets.Count == 0 && installer.SelectedPreset is { } only) _presets.Add(only);
        _index = Math.Max(0, installer.SelectedPreset is null ? 0 : _presets.IndexOf(installer.SelectedPreset));
        _current = _presets.Count > 0 ? _presets[_index] : null;

        // "Install" follows the Install page: disabled while it's busy or no game is found.
        installer.PropertyChanged += OnInstallerChanged;
    }

    private void OnInstallerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstallerViewModel.IsBusy) or nameof(InstallerViewModel.SelectedInstallation)
            or nameof(InstallerViewModel.SelectedPreset))
            InstallCommand.NotifyCanExecuteChanged();
    }

    private bool CanInstall() => Current is not null && !Installer.IsBusy && Installer.SelectedInstallation is not null;

    /// <summary>The Install page: install command, progress and status are shared with it.</summary>
    public InstallerViewModel Installer { get; }

    public ObservableCollection<LogEntry> Log => _log.Entries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private int _index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private Preset? _current;

    /// <summary>"12 / 168"</summary>
    public string PositionText => _presets.Count == 0 ? "" : $"{Index + 1} / {_presets.Count}";

    /// <summary>"Koda · Coldwind Farm"</summary>
    public string? Details => Current is null ? null
        : string.Join(" · ", new[] { Current.Author, Current.Map }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool CanBrowse => _presets.Count > 1;

    [ObservableProperty] private BitmapImage? _image;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _message;

    /// <summary>The log panel above the bar is open.</summary>
    [ObservableProperty] private bool _showLog;

    public event EventHandler? RequestClose;

    public Task LoadAsync() => LoadCurrentAsync();

    private async Task LoadCurrentAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        Image = null;
        Message = null;

        if (Current is not { } preset) return;
        Installer.SelectedPreset = preset; // the Install page follows, so "Install" installs this one

        if (PresetRepository.GetPreviewPath(preset) is not { } path)
        {
            Message = Loc.Get("Inst.NoPreview");
            return;
        }

        IsLoading = true;
        try
        {
            if (!File.Exists(path)) await _github.DownloadPreviewAsync(Path.GetFileName(path), path, cts.Token);
            var image = await Task.Run(() => ImageLoader.Load(path), cts.Token);
            if (cts.IsCancellationRequested) return;
            Image = image;
            _ = PrefetchAsync(Index + 1); // the next one is usually where the user goes
        }
        catch (OperationCanceledException)
        {
            // Moved on to another preset.
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
                Message = Loc.Get(ex is HttpRequestException ? "Inst.PreviewOffline" : "Full.Failed");
        }
        finally
        {
            if (!cts.IsCancellationRequested) IsLoading = false;
        }
    }

    /// <summary>Downloads a neighbour's preview in the background, so the arrow shows it at once.</summary>
    private async Task PrefetchAsync(int index)
    {
        if (_presets.Count < 2) return;
        var preset = _presets[(index % _presets.Count + _presets.Count) % _presets.Count];
        if (PresetRepository.GetPreviewPath(preset) is not { } path || File.Exists(path)) return;
        try { await _github.DownloadPreviewAsync(Path.GetFileName(path), path); }
        catch { /* it'll be tried again when shown */ }
    }

    private void Go(int delta)
    {
        if (_presets.Count < 2) return;
        Index = ((Index + delta) % _presets.Count + _presets.Count) % _presets.Count;
        Current = _presets[Index];
        _ = LoadCurrentAsync();
    }

    [RelayCommand]
    private void Previous() => Go(-1);

    [RelayCommand]
    private void Next() => Go(+1);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (Current is null) return;
        Installer.SelectedPreset = Current;
        if (Installer.InstallCommand.CanExecute(null)) await Installer.InstallCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Current is null) return;
        Installer.ToggleFavoriteCommand.Execute(Current);
    }

    [RelayCommand]
    private void ToggleLog() => ShowLog = !ShowLog;

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    /// <summary>Called when the window closes (however it's closed).</summary>
    public void Detach()
    {
        _loadCts?.Cancel();
        Installer.PropertyChanged -= OnInstallerChanged;
    }
}
