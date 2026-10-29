using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Models;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>One preset in the "New presets" window, with a small preview loaded in the background.</summary>
public sealed partial class NewPresetItem : ObservableObject
{
    public NewPresetItem(Preset preset) => Preset = preset;

    public Preset Preset { get; }

    /// <summary>"Koda · Coldwind Farm"</summary>
    public string? Details => string.Join(" · ", new[] { Preset.Author, Preset.Map }.Where(s => !string.IsNullOrWhiteSpace(s)));

    [ObservableProperty] private BitmapImage? _thumbnail;
    [ObservableProperty] private bool _isLoading = true;
}

/// <summary>Window shown at start-up when the preset sync brought new presets.</summary>
public sealed partial class NewPresetsViewModel : ObservableObject
{
    private readonly GitHubService _github;
    private readonly SettingsService _settings;
    private readonly Action<Preset> _show;
    private readonly Action _showAllNew;

    public NewPresetsViewModel(IEnumerable<Preset> presets, GitHubService github, SettingsService settings,
        Action<Preset> show, Action showAllNew)
    {
        _github = github;
        _settings = settings;
        _show = show;
        _showAllNew = showAllNew;
        foreach (var p in presets) Items.Add(new NewPresetItem(p));
        Title = Loc.Format("NewPresets.Title", Items.Count);
        _showOnStartup = settings.Current.ShowNewPresetsWindow;
    }

    public ObservableCollection<NewPresetItem> Items { get; } = new();

    public string Title { get; }

    /// <summary>"Show this window when new presets arrive".</summary>
    [ObservableProperty] private bool _showOnStartup;

    partial void OnShowOnStartupChanged(bool value)
    {
        _settings.Current.ShowNewPresetsWindow = value;
        _settings.Save();
    }

    public event EventHandler? RequestClose;

    /// <summary>Loads the small previews, a few at a time (they may need downloading first).</summary>
    public async Task LoadThumbnailsAsync()
    {
        using var gate = new SemaphoreSlim(3);
        await Task.WhenAll(Items.Select(async item =>
        {
            await gate.WaitAsync();
            try
            {
                if (PresetRepository.GetPreviewPath(item.Preset) is { } path)
                {
                    if (!File.Exists(path)) await _github.DownloadPreviewAsync(Path.GetFileName(path), path);
                    item.Thumbnail = await Task.Run(() => ImageLoader.Load(path, decodePixelWidth: 320));
                }
            }
            catch
            {
                // Offline or no preview: the card shows the placeholder.
            }
            finally
            {
                item.IsLoading = false;
                gate.Release();
            }
        }));
    }

    [RelayCommand]
    private void Show(NewPresetItem? item)
    {
        if (item is null) return;
        _show(item.Preset);
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowAll()
    {
        _showAllNew();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);
}
