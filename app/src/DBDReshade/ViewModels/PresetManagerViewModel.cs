using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Models;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>Add your own presets (description, source, screenshot), delete presets, restore deleted ones.</summary>
public sealed partial class PresetManagerViewModel : ObservableObject
{
    private readonly PresetRepository _repository;
    private readonly PresetSyncService _sync;
    private readonly LogService _log;

    public PresetManagerViewModel(PresetRepository repository, PresetSyncService sync, LogService log)
    {
        _repository = repository;
        _sync = sync;
        _log = log;
        _repository.Changed += (_, _) => ReloadPresets();
        ReloadPresets();
    }

    public ObservableCollection<Preset> Presets { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private Preset? _selectedPreset;

    /// <summary>Official presets the user deleted (hidden from sync until restored).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeletedPresets))]
    private int _deletedCount;

    public bool HasDeletedPresets => DeletedCount > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreDeletedCommand), nameof(DeleteCommand), nameof(SaveCommand))]
    private bool _isBusy;

    // ---- New preset form -------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _iniPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _description = "";

    [ObservableProperty]
    private string _source = "";

    [ObservableProperty]
    private string? _screenshotPath;

    [ObservableProperty]
    private BitmapImage? _screenshotPreview;

    partial void OnScreenshotPathChanged(string? value)
    {
        try
        {
            ScreenshotPreview = value is null ? null : ImageLoader.Load(value, decodePixelWidth: 640);
        }
        catch (Exception ex)
        {
            ScreenshotPreview = null;
            _log.Warning($"Cannot read image: {ex.Message}");
        }
    }

    [RelayCommand]
    private void BrowseIni()
    {
        var path = Dialogs.PickFile(Loc.Get("Man.PickIni"), $"{Loc.Get("Man.IniFilter")} (*.ini)|*.ini");
        if (path is null) return;

        IniPath = path;
        _log.Info($"Selected preset file: {path}");
    }

    [RelayCommand]
    private void BrowseScreenshot()
    {
        var path = Dialogs.PickFile(Loc.Get("Man.PickImage"),
            $"{Loc.Get("Man.ImageFilter")} (*.jpg;*.jpeg;*.png;*.gif)|*.jpg;*.jpeg;*.png;*.gif");
        if (path is not null) ScreenshotPath = path;
    }

    [RelayCommand]
    private void ClearScreenshot() => ScreenshotPath = null;

    private bool CanSave() => !IsBusy && !string.IsNullOrWhiteSpace(IniPath) && !string.IsNullOrWhiteSpace(Description);

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

        var fileName = Path.GetFileName(IniPath!);

        if (_repository.IsOfficial(fileName) && !_repository.IsCustom(fileName))
        {
            // Same name as a preset from GitHub: by default keep both, so the official one keeps updating.
            switch (Dialogs.YesNoCancel(Loc.Format("Man.ConflictOfficial", Path.GetFileNameWithoutExtension(fileName))))
            {
                case true:
                    fileName = _repository.GetFreeCopyName(fileName, Loc.Get("Common.Custom"));
                    break;
                case false:
                    break; // replace the official preset on this PC
                default:
                    return;
            }
        }
        else if (_repository.Exists(fileName) && !Dialogs.Confirm(Loc.Format("Man.Exists", fileName)))
        {
            return;
        }

        try
        {
            _repository.AddCustom(IniPath!, fileName, Description, Source, ScreenshotPath);
            _log.Success($"Preset {fileName} saved.");
            Dialogs.Info(Loc.Format("Man.Saved", Path.GetFileNameWithoutExtension(fileName)));

            IniPath = null;
            Description = "";
            Source = "";
            ScreenshotPath = null;
            SelectedPreset = Presets.FirstOrDefault(p => p.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _log.Error($"Saving preset failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Man.SaveFailed", ex.Message));
        }
    }

    // ---- Delete / restore ------------------------------------------------------------------

    private bool CanDelete() => !IsBusy && SelectedPreset is not null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var preset = SelectedPreset!;
        var restoresOfficial = preset.IsCustom && _repository.IsOfficial(preset.FileName);

        var question = preset.IsCustom
            ? Loc.Format(restoresOfficial ? "Man.ConfirmDeleteOverride" : "Man.ConfirmDeleteCustom", preset.DisplayName)
            : Loc.Format("Man.ConfirmDelete", preset.DisplayName);
        if (!Dialogs.Confirm(question)) return;

        try
        {
            _repository.Delete(preset);
            _log.Info($"Preset {preset.FileName} deleted.");

            // The user's copy was hiding an official preset: bring the official one back right away.
            if (restoresOfficial) await SyncQuietlyAsync();
        }
        catch (Exception ex)
        {
            _log.Error($"Deleting preset failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Man.DeleteFailed", ex.Message));
        }
    }

    private bool CanRestore() => !IsBusy && DeletedCount > 0;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreDeletedAsync()
    {
        var count = _repository.RestoreDeleted();
        _log.Info($"Restoring {count} deleted preset(s).");

        if (await SyncQuietlyAsync())
            Dialogs.Info(Loc.Format("Man.Restored", count));
        else
            Dialogs.Warning(Loc.Get("Man.RestoreOffline"));
    }

    private async Task<bool> SyncQuietlyAsync()
    {
        IsBusy = true;
        try
        {
            await _sync.SyncAsync();
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning($"Sync failed: {ex.Message}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReloadPresets()
    {
        var selected = SelectedPreset?.FileName;
        Presets.Clear();
        foreach (var preset in _repository.Presets) Presets.Add(preset);
        SelectedPreset = Presets.FirstOrDefault(p => p.FileName == selected);
        DeletedCount = _repository.DeletedCount;
        RestoreDeletedCommand.NotifyCanExecuteChanged();
    }
}
