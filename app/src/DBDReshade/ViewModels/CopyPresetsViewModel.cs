using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Models;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>Copies any number of presets to a folder of the user's choice.</summary>
public sealed partial class CopyPresetsViewModel : ObservableObject
{
    private readonly PresetRepository _repository;
    private readonly LogService _log;

    public CopyPresetsViewModel(PresetRepository repository, LogService log)
    {
        _repository = repository;
        _log = log;
        _repository.Changed += (_, _) => ReloadPresets();
        Loc.Changed += (_, _) => OnPropertyChanged(nameof(ToggleAllText));
        ReloadPresets();
    }

    public ObservableCollection<SelectablePreset> Items { get; } = new();

    public int SelectedCount => Items.Count(i => i.IsSelected);

    public bool AllSelected => Items.Count > 0 && Items.All(i => i.IsSelected);

    public string ToggleAllText => Loc.Get(AllSelected ? "Copy.DeselectAll" : "Copy.SelectAll");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    private string? _destinationFolder;

    [RelayCommand]
    private void ToggleAll()
    {
        var select = !AllSelected;
        foreach (var item in Items) item.IsSelected = select;
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        var folder = Dialogs.PickFolder(Loc.Get("Copy.PickTitle"),
            DestinationFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (folder is not null) DestinationFolder = folder;
    }

    private bool CanCopy() => SelectedCount > 0 && !string.IsNullOrWhiteSpace(DestinationFolder);

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private async Task CopyAsync()
    {
        var folder = DestinationFolder!;
        var selected = Items.Where(i => i.IsSelected).Select(i => i.Preset).ToList();

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(folder);
                foreach (var preset in selected)
                    File.Copy(preset.FilePath, Path.Combine(folder, preset.FileName), overwrite: true);
            });

            _log.Success($"Copied {selected.Count} preset(s) to {folder}");
            Dialogs.Info(Loc.Format("Copy.Done", selected.Count, folder));
        }
        catch (Exception ex)
        {
            _log.Error($"Copy failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Copy.Failed", ex.Message));
        }
    }

    [RelayCommand]
    private void OpenDestination() => Shell.Open(DestinationFolder);

    private void ReloadPresets()
    {
        var previouslySelected = Items.Where(i => i.IsSelected)
            .Select(i => i.Preset.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Items) item.PropertyChanged -= OnItemChanged;
        Items.Clear();

        foreach (var preset in _repository.Presets)
        {
            var item = new SelectablePreset(preset) { IsSelected = previouslySelected.Contains(preset.FileName) };
            item.PropertyChanged += OnItemChanged;
            Items.Add(item);
        }
        RaiseSelectionChanged();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectablePreset.IsSelected)) RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(ToggleAllText));
        CopyCommand.NotifyCanExecuteChanged();
    }
}
