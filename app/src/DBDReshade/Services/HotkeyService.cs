using DBDReshade.Models;

namespace DBDReshade.Services;

public enum HotkeyApplyResult { Applied, NoGame, NoReShade, GameRunning, Failed }

/// <summary>
/// Keyboard shortcuts that switch preset in game. They are ReShade's own "preset shortcuts"
/// (PresetShortcutKeys / PresetShortcutPaths in ReShade.ini), so they work exactly like setting them in
/// ReShade's overlay: press the key during a match and ReShade loads that preset.
/// The presets are copied into the game's preset folder, where ReShade can find them.
/// </summary>
public sealed class HotkeyService
{
    private readonly SettingsService _settings;
    private readonly PresetRepository _repository;
    private readonly ReShadeService _reshade;
    private readonly LogService _log;

    private GameInstallation? _game;
    private string? _presetFolder;

    public HotkeyService(SettingsService settings, PresetRepository repository, ReShadeService reshade, LogService log)
    {
        _settings = settings;
        _repository = repository;
        _reshade = reshade;
        _log = log;
    }

    public IReadOnlyList<PresetHotkey> Hotkeys => _settings.Current.Hotkeys;

    /// <summary>Result of the last attempt to write them into the game (shown in Settings).</summary>
    public HotkeyApplyResult? LastResult { get; private set; }

    /// <summary>The game the shortcuts go to, and where its presets are (set by the Install page).</summary>
    public GameInstallation? Game => _game;

    public event EventHandler? ResultChanged;

    public void SetTarget(GameInstallation? game, string? presetFolder)
    {
        _game = game;
        _presetFolder = presetFolder;
    }

    /// <summary>Saves the shortcuts and writes them into the game's ReShade.ini.</summary>
    public HotkeyApplyResult Save(IEnumerable<PresetHotkey> hotkeys)
    {
        _settings.Current.Hotkeys = hotkeys.Where(h => h.Key != 0 && h.PresetFileName.Length > 0).ToList();
        _settings.Save();
        return Apply();
    }

    /// <summary>Writes the saved shortcuts into the game's ReShade.ini (the game must be closed).</summary>
    public HotkeyApplyResult Apply()
    {
        var result = ApplyCore();
        LastResult = result;
        ResultChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private HotkeyApplyResult ApplyCore()
    {
        if (_game is null || string.IsNullOrWhiteSpace(_presetFolder)) return HotkeyApplyResult.NoGame;
        if (!_reshade.IsInstalled(_game)) return HotkeyApplyResult.NoReShade;
        // ReShade rewrites ReShade.ini when the game closes: changes made now would be lost.
        if (ReShadeService.IsGameRunning(_game)) return HotkeyApplyResult.GameRunning;

        try
        {
            var keys = new List<string>();
            var paths = new List<string>();
            foreach (var hotkey in Hotkeys)
            {
                var preset = _repository.Presets.FirstOrDefault(p =>
                    string.Equals(p.FileName, hotkey.PresetFileName, StringComparison.OrdinalIgnoreCase));
                if (preset is null || hotkey.Key == 0) continue;

                // The preset must be where ReShade looks. An existing copy is kept: it may have the
                // user's own tweaks made in ReShade's overlay.
                Directory.CreateDirectory(_presetFolder!);
                var target = Path.Combine(_presetFolder!, preset.FileName);
                if (!File.Exists(target)) File.Copy(preset.FilePath, target);

                keys.Add($"{hotkey.Key},{(hotkey.Ctrl ? 1 : 0)},{(hotkey.Shift ? 1 : 0)},{(hotkey.Alt ? 1 : 0)}");
                paths.Add(target.Replace(",", ",,")); // ReShade escapes commas in lists by doubling them
            }

            IniFile.SetValue(_game.ReShadeIniPath, "GENERAL", "PresetShortcutKeys", string.Join(",", keys));
            IniFile.SetValue(_game.ReShadeIniPath, "GENERAL", "PresetShortcutPaths", string.Join(",", paths));
            _log.Info($"Preset shortcuts written to ReShade.ini: {paths.Count}");
            return HotkeyApplyResult.Applied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning($"Preset shortcuts could not be written: {ex.Message}");
            return HotkeyApplyResult.Failed;
        }
    }
}
