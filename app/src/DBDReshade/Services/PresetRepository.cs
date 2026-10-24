using System.Text.Encodings.Web;
using System.Text.Json;
using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>
/// Local preset library. Metadata comes from two files: presets.json (mirrored from GitHub)
/// and user-presets.json (presets the user added). User entries win on conflicts.
/// </summary>
public sealed class PresetRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Forgive the two most common hand-editing mistakes in presets.json.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly SettingsService _settings;
    private Dictionary<string, PresetMetadata> _remote = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PresetMetadata> _user = new(StringComparer.OrdinalIgnoreCase);

    public PresetRepository(SettingsService settings) => _settings = settings;

    /// <summary>Raised whenever presets are added, removed or their metadata changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<Preset> Presets { get; private set; } = Array.Empty<Preset>();

    public void Reload()
    {
        _remote = ReadMetadata(AppPaths.RemoteMetadataFile);
        _user = ReadMetadata(AppPaths.UserMetadataFile);

        Presets = Directory.EnumerateFiles(AppPaths.PresetsDirectory, "*.ini")
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var isCustom = _user.TryGetValue(name, out var meta);
                if (!isCustom) _remote.TryGetValue(name, out meta);
                return new Preset(path, meta, isCustom);
            })
            .OrderBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Exists(string fileName) =>
        File.Exists(Path.Combine(AppPaths.PresetsDirectory, fileName));

    public bool IsIgnored(string fileName) =>
        _settings.Current.IgnoredPresets.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>True for presets listed in the GitHub presets.json.</summary>
    public bool IsOfficial(string fileName) => _remote.ContainsKey(fileName);

    /// <summary>Number of official presets the user deleted.</summary>
    public int DeletedCount => _settings.Current.IgnoredPresets.Count;

    /// <summary>Forgets every deletion, so the next sync downloads those presets again.</summary>
    /// <returns>How many presets will come back.</returns>
    public int RestoreDeleted()
    {
        var count = _settings.Current.IgnoredPresets.Count;
        _settings.Current.IgnoredPresets.Clear();
        _settings.Save();
        return count;
    }

    /// <summary>"Name.ini" → "Name (custom).ini", "Name (custom 2).ini"… whichever is free.</summary>
    public string GetFreeCopyName(string fileName, string suffix)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var candidate = $"{name} ({suffix}){ext}";
        for (var i = 2; Exists(candidate) || IsOfficial(candidate); i++)
            candidate = $"{name} ({suffix} {i}){ext}";
        return candidate;
    }

    /// <summary>True for presets the user added: the GitHub sync never overwrites them.</summary>
    public bool IsCustom(string fileName) => _user.ContainsKey(fileName);

    /// <summary>
    /// Preview images of the user's own presets. They exist only on this PC,
    /// so cleanup and sync must never delete them.
    /// </summary>
    public IReadOnlySet<string> UserPreviewFiles =>
        _user.Values
            .Select(m => m.PreviewImage)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Path.GetFileName(name!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Replaces the GitHub metadata file after validating that it parses.</summary>
    public void SaveRemoteMetadata(string json)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, PresetMetadata>>(json, JsonOptions)
            ?? throw new InvalidDataException("presets.json is empty.");
        File.WriteAllText(AppPaths.RemoteMetadataFile, JsonSerializer.Serialize(parsed, JsonOptions));
    }

    /// <summary>Path of the cached preview image, or null if the preset has none.</summary>
    public static string? GetPreviewPath(Preset preset) =>
        string.IsNullOrWhiteSpace(preset.Metadata?.PreviewImage)
            ? null
            : Path.Combine(AppPaths.PreviewsDirectory, Path.GetFileName(preset.Metadata!.PreviewImage!));

    /// <summary>Imports a user preset: copies the .ini (and screenshot) and writes its metadata.</summary>
    /// <param name="fileName">Name to store it under (may differ from the source file, e.g. a "(custom)" copy).</param>
    public void AddCustom(string iniPath, string fileName, string description, string? source, string? screenshotPath)
    {
        File.Copy(iniPath, Path.Combine(AppPaths.PresetsDirectory, fileName), overwrite: true);

        string? previewName = null;
        if (!string.IsNullOrWhiteSpace(screenshotPath))
        {
            // Name the preview after the preset so two presets can't overwrite each other's image.
            previewName = Path.GetFileNameWithoutExtension(fileName) + Path.GetExtension(screenshotPath);
            File.Copy(screenshotPath, Path.Combine(AppPaths.PreviewsDirectory, previewName), overwrite: true);
        }

        _user[fileName] = new PresetMetadata
        {
            Description = description.Trim(),
            Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
            PreviewImage = previewName,
        };
        WriteMetadata(AppPaths.UserMetadataFile, _user);

        _settings.Current.IgnoredPresets.RemoveAll(n => n.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        _settings.Save();

        Reload();
    }

    /// <summary>
    /// Deletes a preset. An official one is remembered so the sync won't bring it back (until restored);
    /// a user preset is simply removed, which also lets an official preset with the same name return.
    /// </summary>
    public void Delete(Preset preset)
    {
        File.Delete(preset.FilePath);

        if (preset.IsCustom)
        {
            _user.Remove(preset.FileName);
            WriteMetadata(AppPaths.UserMetadataFile, _user);
            if (!string.IsNullOrWhiteSpace(preset.Metadata?.PreviewImage))
                TryDelete(Path.Combine(AppPaths.PreviewsDirectory, Path.GetFileName(preset.Metadata!.PreviewImage!)));
        }
        else if (!IsIgnored(preset.FileName))
        {
            _settings.Current.IgnoredPresets.Add(preset.FileName);
            _settings.Save();
        }

        Reload();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { /* in use: harmless leftover */ }
    }

    private static Dictionary<string, PresetMetadata> ReadMetadata(string path)
    {
        var result = new Dictionary<string, PresetMetadata>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path)) return result;
            var parsed = JsonSerializer.Deserialize<Dictionary<string, PresetMetadata>>(File.ReadAllText(path), JsonOptions);
            if (parsed is null) return result;
            foreach (var (key, value) in parsed) result[key] = value;
        }
        catch
        {
            // Corrupted metadata only costs descriptions/previews, not presets.
        }
        return result;
    }

    private static void WriteMetadata(string path, Dictionary<string, PresetMetadata> data) =>
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
}
