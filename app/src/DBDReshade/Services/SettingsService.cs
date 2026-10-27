using System.Text.Json;
using DBDReshade.Models;

namespace DBDReshade.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    /// <summary>No settings file yet: the app has never run on this PC (not an update).</summary>
    public bool IsFirstRun { get; private set; }

    public void Load()
    {
        IsFirstRun = !File.Exists(AppPaths.SettingsFile);
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new();
        }
        catch
        {
            Current = new AppSettings(); // corrupted file: start fresh
        }
    }

    public void Save()
    {
        try
        {
            // Write a copy first: a crash or full disk mid-write must not wipe the settings
            // (they include the list of deleted presets).
            var temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch
        {
            // Non-critical.
        }
    }
}
