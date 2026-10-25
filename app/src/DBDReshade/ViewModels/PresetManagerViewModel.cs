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
    private readonly PresetPackageService _packages;

    public PresetManagerViewModel(PresetRepository repository, PresetSyncService sync, PresetPackageService packages, LogService log)
    {
        _packages = packages;
        _repository = repository;
        _sync = sync;
        _log = log;
        _repository.Changed += (_, _) => ReloadPresets();
        ReloadPresets();
    }

    public ObservableCollection<Preset> Presets { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
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

        var fileName = SaveToLibrary(IniPath!, Path.GetFileName(IniPath!), Description, Source, ScreenshotPath);
        if (fileName is null) return;

        Dialogs.Info(Loc.Format("Man.Saved", Path.GetFileNameWithoutExtension(fileName)));
        IniPath = null;
        Description = "";
        Source = "";
        ScreenshotPath = null;
        SelectedPreset = Presets.FirstOrDefault(p => p.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a preset to the library, asking what to do when the name is taken.
    /// </summary>
    /// <returns>The file name it was saved under, or null if the user cancelled or saving failed.</returns>
    private string? SaveToLibrary(string iniPath, string fileName, string description, string? source, string? screenshot,
        PresetMetadata? extra = null)
    {
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
                    return null;
            }
        }
        else if (_repository.Exists(fileName) && !Dialogs.Confirm(Loc.Format("Man.Exists", fileName)))
        {
            return null;
        }

        try
        {
            _repository.AddCustom(iniPath, fileName, description, source, screenshot, extra);
            _log.Success($"Preset {fileName} saved.");
            return fileName;
        }
        catch (Exception ex)
        {
            _log.Error($"Saving preset failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Man.SaveFailed", ex.Message));
            return null;
        }
    }

    // ---- Export / import -------------------------------------------------------------------

    private bool CanExport() => SelectedPreset is not null;

    /// <summary>Saves the selected preset as one .dbdpreset file (preset, screenshot and description).</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        var preset = SelectedPreset!;
        var path = Dialogs.SaveFile(Loc.Get("Man.ExportTitle"),
            $"{Loc.Get("Man.PackageFilter")} (*{PresetPackageService.Extension})|*{PresetPackageService.Extension}",
            preset.DisplayName + PresetPackageService.Extension);
        if (path is null) return;

        try
        {
            _packages.Export(preset, path);
            Shell.ShowInFolder(path);
        }
        catch (Exception ex)
        {
            _log.Error($"Export failed: {ex.Message}");
            Dialogs.Error(Loc.Format("Man.ExportFailed", ex.Message));
        }
    }

    [RelayCommand]
    private void Import()
    {
        var files = Dialogs.PickFiles(Loc.Get("Man.ImportTitle"),
            $"{Loc.Get("Man.PackageFilter")} (*{PresetPackageService.Extension};*.zip;*.ini)|*{PresetPackageService.Extension};*.zip;*.ini");
        if (files.Count > 0) ImportFiles(files);
    }

    /// <summary>Imports preset packages (or plain .ini files): from the Import button or dropped on the window.</summary>
    public void ImportFiles(IEnumerable<string> paths)
    {
        var imported = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var package = _packages.Open(path);
                var source = Uri.TryCreate(package.Metadata.Source, UriKind.Absolute, out var uri)
                             && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    ? package.Metadata.Source : null;
                var saved = SaveToLibrary(package.IniPath, package.FileName, package.Metadata.Description ?? "",
                    source, package.ScreenshotPath, package.Metadata);
                if (saved is not null) imported.Add(Path.GetFileNameWithoutExtension(saved));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _log.Error($"Import of {Path.GetFileName(path)} failed: {ex.Message}");
                Dialogs.Error(Loc.Format("Man.ImportFailed", Path.GetFileName(path), ex.Message));
            }
        }

        if (imported.Count == 0) return;
        SelectedPreset = Presets.FirstOrDefault(p => p.DisplayName == imported[^1]);
        Dialogs.Info(Loc.Format("Man.Imported", imported.Count, string.Join(", ", imported)));
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
