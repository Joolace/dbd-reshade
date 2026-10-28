using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Models;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>One row of Settings → In-game shortcuts: a preset and its key.</summary>
public sealed partial class HotkeyRow : ObservableObject
{
    private readonly Action _changed;

    public HotkeyRow(Action changed, Preset? preset, int key, bool ctrl, bool shift, bool alt)
    {
        _changed = changed;
        _preset = preset;
        _keyCode = key;
        _ctrl = ctrl;
        _shift = shift;
        _alt = alt;
    }

    [ObservableProperty] private Preset? _preset;
    [ObservableProperty] private int _keyCode;
    [ObservableProperty] private bool _ctrl;
    [ObservableProperty] private bool _shift;
    [ObservableProperty] private bool _alt;

    partial void OnPresetChanged(Preset? value) => _changed();
    partial void OnKeyCodeChanged(int value) => _changed();
    partial void OnCtrlChanged(bool value) => _changed();
    partial void OnShiftChanged(bool value) => _changed();
    partial void OnAltChanged(bool value) => _changed();

    public string Signature => $"{KeyCode}|{Ctrl}|{Shift}|{Alt}";
}

/// <summary>Settings → In-game shortcuts: keys that switch preset during a match (written into ReShade.ini).</summary>
public sealed partial class HotkeysViewModel : ObservableObject
{
    /// <summary>F5 to F11: free in Dead by Daylight and in ReShade by default.</summary>
    private static readonly int[] SuggestedKeys = { 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A };
    private const int MaxRows = 12;

    private readonly HotkeyService _hotkeys;
    private readonly PresetRepository _repository;
    private bool _loading;

    public HotkeysViewModel(HotkeyService hotkeys, PresetRepository repository)
    {
        _hotkeys = hotkeys;
        _repository = repository;
        _repository.Changed += (_, _) => Reload();
        _hotkeys.ResultChanged += (_, _) => UpdateStatus();
        Loc.Changed += (_, _) => UpdateStatus();
        Reload();
    }

    /// <summary>Every preset in the library, by name (for the drop-downs).</summary>
    public ObservableCollection<Preset> Presets { get; } = new();

    public ObservableCollection<HotkeyRow> Rows { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _statusIsWarning;
    [ObservableProperty] private string? _duplicateWarning;

    private void Reload()
    {
        _loading = true;
        try
        {
            Presets.Clear();
            foreach (var p in _repository.Presets.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)) Presets.Add(p);

            Rows.Clear();
            foreach (var h in _hotkeys.Hotkeys)
                Rows.Add(new HotkeyRow(OnRowChanged, Find(h.PresetFileName), h.Key, h.Ctrl, h.Shift, h.Alt));
        }
        finally
        {
            _loading = false;
        }
        AddCommand.NotifyCanExecuteChanged();
        UpdateStatus();
    }

    private Preset? Find(string fileName) =>
        Presets.FirstOrDefault(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    private void OnRowChanged()
    {
        if (_loading) return;
        Save();
    }

    private void Save()
    {
        // The same key twice: ReShade would only ever use the first one.
        DuplicateWarning = Rows.Where(r => r.KeyCode != 0).GroupBy(r => r.Signature).Any(g => g.Count() > 1)
            ? Loc.Get("Hotkeys.Duplicate") : null;

        _hotkeys.Save(Rows.Where(r => r.Preset is not null && r.KeyCode != 0).Select(r => new PresetHotkey
        {
            PresetFileName = r.Preset!.FileName, Key = r.KeyCode, Ctrl = r.Ctrl, Shift = r.Shift, Alt = r.Alt,
        }));
        AddCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var used = Rows.Select(r => r.KeyCode).ToHashSet();
        var key = SuggestedKeys.FirstOrDefault(k => !used.Contains(k));
        Rows.Add(new HotkeyRow(OnRowChanged, null, key, false, false, false));
        AddCommand.NotifyCanExecuteChanged();
    }

    private bool CanAdd() => Rows.Count < MaxRows;

    [RelayCommand]
    private void Remove(HotkeyRow? row)
    {
        if (row is null) return;
        Rows.Remove(row);
        Save();
    }

    /// <summary>Gives each favourite without a key one of F5-F11.</summary>
    [RelayCommand]
    private void UseFavorites()
    {
        var favorites = _repository.Presets.Where(p => p.IsFavorite)
            .Where(p => Rows.All(r => r.Preset?.FileName != p.FileName)).ToList();
        if (favorites.Count == 0)
        {
            Status = Loc.Get("Hotkeys.NoFavorites");
            StatusIsWarning = true;
            return;
        }

        _loading = true;
        var used = Rows.Select(r => r.KeyCode).ToHashSet();
        foreach (var preset in favorites)
        {
            if (Rows.Count >= MaxRows) break;
            var key = SuggestedKeys.FirstOrDefault(k => !used.Contains(k));
            if (key == 0) break;
            used.Add(key);
            Rows.Add(new HotkeyRow(OnRowChanged, Find(preset.FileName), key, false, false, false));
        }
        _loading = false;
        Save();
    }

    [RelayCommand]
    private void Apply() => _hotkeys.Apply();

    private void UpdateStatus()
    {
        StatusIsWarning = _hotkeys.LastResult is not (null or HotkeyApplyResult.Applied);
        Status = _hotkeys.LastResult switch
        {
            HotkeyApplyResult.Applied => Rows.Count == 0 ? null : Loc.Format("Hotkeys.Applied", _hotkeys.Game?.Store ?? ""),
            HotkeyApplyResult.NoGame => Loc.Get("Hotkeys.NoGame"),
            HotkeyApplyResult.NoReShade => Loc.Get("Hotkeys.NoReShade"),
            HotkeyApplyResult.GameRunning => Loc.Get("Hotkeys.GameRunning"),
            HotkeyApplyResult.Failed => Loc.Get("Hotkeys.Failed"),
            _ => null,
        };
    }
}
