using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>
/// Settings → Repair: fixes the library by hand when something got out of step, e.g. presets that arrived
/// without description or preview because presets.json was out of date or damaged.
/// </summary>
public sealed partial class RepairViewModel : ObservableObject
{
    private readonly GitHubService _github;
    private readonly PresetRepository _repository;
    private readonly PresetSyncService _sync;
    private readonly TranslationSyncService _translations;
    private readonly LogService _log;

    public RepairViewModel(GitHubService github, PresetRepository repository, PresetSyncService sync,
        TranslationSyncService translations, LogService log)
    {
        _github = github;
        _repository = repository;
        _sync = sync;
        _translations = translations;
        _log = log;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RepairMetadataCommand), nameof(VerifyPresetsCommand), nameof(RepairTranslationsCommand))]
    private bool _isWorking;

    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _statusIsError;

    private bool CanRun() => !IsWorking;

    /// <summary>Downloads presets.json again (always the current one) and reloads descriptions and previews.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RepairMetadataAsync() => RunAsync(async () =>
    {
        var json = await _github.GetPresetMetadataJsonAsync();
        _repository.SaveRemoteMetadata(json); // throws if it's not valid JSON: the old file is kept
        _repository.Reload();
        var missing = _repository.Presets.Count(p => !p.IsCustom && p.Metadata is null);
        _log.Success($"presets.json repaired: {_repository.Presets.Count - missing} presets with description.");
        return missing == 0
            ? Loc.Get("Repair.MetadataDone")
            : Loc.Format("Repair.MetadataPartial", missing);
    });

    /// <summary>Checks every official preset against GitHub: missing or changed files are downloaded again.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task VerifyPresetsAsync() => RunAsync(async () =>
    {
        var result = await _sync.SyncAsync();
        return result.ChangedPresets > 0
            ? Loc.Format("Repair.PresetsFixed", result.NewPresets, result.UpdatedPresets)
            : Loc.Get("Repair.PresetsOk");
    });

    /// <summary>Downloads the interface translations again.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RepairTranslationsAsync() => RunAsync(async () =>
    {
        var changed = await _translations.SyncAsync();
        return changed > 0 ? Loc.Format("Repair.TranslationsFixed", changed) : Loc.Get("Repair.TranslationsOk");
    });

    private async Task RunAsync(Func<Task<string>> action)
    {
        IsWorking = true;
        StatusIsError = false;
        Status = Loc.Get("Repair.Working");
        try
        {
            Status = await action();
        }
        catch (Exception ex)
        {
            _log.Warning($"Repair failed: {ex.Message}");
            StatusIsError = true;
            Status = Loc.Format("Repair.Failed", ex.Message);
        }
        finally
        {
            IsWorking = false;
        }
    }
}
