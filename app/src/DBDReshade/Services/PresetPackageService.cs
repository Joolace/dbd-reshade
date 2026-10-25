using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>The contents of a preset package, unpacked to a temporary folder.</summary>
public sealed record PresetPackage(string IniPath, string FileName, PresetMetadata Metadata, string? ScreenshotPath);

/// <summary>
/// Preset packages: one file with the .ini, its screenshot and its description, to share on Discord or
/// attach to a GitHub submission. A package is a zip (extension .dbdpreset, or .zip for GitHub, which
/// only accepts common file types) containing:
/// <code>
///   preset.json      { "format": 1, "file": "Name.ini", "description", "videoLink", "author", "map", "tags" }
///   Name.ini
///   preview.jpg|png  (optional)
/// </code>
/// </summary>
public sealed class PresetPackageService
{
    public const string Extension = ".dbdpreset";
    private const string ManifestName = "preset.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly LogService _log;

    public PresetPackageService(LogService log) => _log = log;

    public static bool IsPackage(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes <paramref name="preset"/> (with its preview, if any) to <paramref name="destination"/>.</summary>
    public void Export(Preset preset, string destination) =>
        Export(preset.FilePath, preset.FileName, preset.Metadata ?? new PresetMetadata(),
            PresetRepository.GetPreviewPath(preset) is { } p && File.Exists(p) ? p : null, destination);

    public void Export(string iniPath, string fileName, PresetMetadata metadata, string? screenshotPath, string destination)
    {
        var temp = destination + ".part";
        if (File.Exists(temp)) File.Delete(temp);

        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(iniPath, fileName);

            string? preview = null;
            if (screenshotPath is not null && File.Exists(screenshotPath))
            {
                preview = "preview" + Path.GetExtension(screenshotPath).ToLowerInvariant();
                zip.CreateEntryFromFile(screenshotPath, preview);
            }

            var manifest = new PackageManifest
            {
                File = fileName,
                Description = metadata.Description,
                Source = metadata.Source,
                Author = metadata.Author,
                Map = metadata.Map,
                Tags = metadata.Tags is { Count: > 0 } tags ? tags : null,
                Preview = preview,
            };
            using var writer = new StreamWriter(zip.CreateEntry(ManifestName).Open());
            writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
        }

        File.Move(temp, destination, overwrite: true);
        _log.Success($"Preset exported: {destination}");
    }

    /// <summary>
    /// Unpacks a package to a temporary folder. A plain .ini works too (no description or screenshot).
    /// </summary>
    /// <exception cref="InvalidDataException">The file isn't a preset package.</exception>
    public PresetPackage Open(string path)
    {
        if (path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            return new PresetPackage(path, Path.GetFileName(path), new PresetMetadata(), null);

        var folder = Path.Combine(AppPaths.TempDirectory, "import-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        using var zip = ZipFile.OpenRead(path);
        PackageManifest? manifest = null;
        if (zip.GetEntry(ManifestName) is { } entry)
        {
            using var reader = new StreamReader(entry.Open());
            try { manifest = JsonSerializer.Deserialize<PackageManifest>(reader.ReadToEnd(), JsonOptions); }
            catch (JsonException) { /* fall back to the files themselves */ }
        }

        // The .ini: the one the manifest names, else the only .ini in the archive.
        var inis = zip.Entries.Where(e => e.FullName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).ToList();
        var ini = (manifest?.File is { } named ? inis.FirstOrDefault(e => e.Name.Equals(named, StringComparison.OrdinalIgnoreCase)) : null)
                  ?? (inis.Count == 1 ? inis[0] : null)
                  ?? throw new InvalidDataException(Loc.Get("Pkg.NotAPackage"));

        var fileName = SafeFileName(ini.Name);
        var iniPath = Path.Combine(folder, fileName);
        ini.ExtractToFile(iniPath, overwrite: true);

        string? screenshot = null;
        var image = (manifest?.Preview is { } p ? zip.GetEntry(p) : null)
                    ?? zip.Entries.FirstOrDefault(e => e.Name.StartsWith("preview.", StringComparison.OrdinalIgnoreCase));
        if (image is not null && IsImage(image.Name))
        {
            screenshot = Path.Combine(folder, "preview" + Path.GetExtension(image.Name).ToLowerInvariant());
            image.ExtractToFile(screenshot, overwrite: true);
        }

        var metadata = new PresetMetadata
        {
            Description = manifest?.Description,
            Source = manifest?.Source,
            Author = manifest?.Author,
            Map = manifest?.Map,
            Tags = manifest?.Tags,
        };
        _log.Info($"Opened preset package {Path.GetFileName(path)}: {fileName}");
        return new PresetPackage(iniPath, fileName, metadata, screenshot);
    }

    private static string SafeFileName(string name)
    {
        var safe = string.Concat(Path.GetFileName(name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return safe.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? safe : safe + ".ini";
    }

    private static bool IsImage(string name) =>
        new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
}

internal sealed class PackageManifest
{
    [JsonPropertyName("format")] public int Format { get; set; } = 1;
    [JsonPropertyName("file")] public string? File { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("videoLink")] public string? Source { get; set; }
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("map")] public string? Map { get; set; }
    [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
    [JsonPropertyName("preview")] public string? Preview { get; set; }
}
