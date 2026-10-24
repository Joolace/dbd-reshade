using System.Text.Json;
using DBDReshade.Models;

namespace DBDReshade.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
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
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch
        {
            // Non-critical.
        }
    }
}
