using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>A saved copy of a game's ReShade setup, taken before the app changed it.</summary>
/// <param name="Reason">"install", "update" or "uninstall" (localized as "Backup.Reason.*").</param>
/// <param name="PresetName">The preset that was active then, if any.</param>
public sealed record BackupEntry(string Folder, DateTime Created, string Reason, string? PresetName);

/// <summary>
/// Before every install, ReShade update or uninstall the app saves the game's ReShade.ini, the active
/// preset and the ReShade DLL to %LOCALAPPDATA%\DBDReshade\Backups\&lt;game&gt;\&lt;time&gt;, so one click
/// puts everything back the way it was. The last <see cref="Keep"/> backups per game are kept.
/// </summary>
public sealed class BackupService
{
    private const int Keep = 5;
    private const string ManifestName = "backup.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly LogService _log;

    public BackupService(LogService log) => _log = log;

    private static string Root => Path.Combine(AppPaths.DataRoot, "Backups");

    /// <summary>One folder per installation, named after a short hash of its path.</summary>
    private static string GameFolder(GameInstallation game)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(game.RootPath.ToLowerInvariant()));
        return Path.Combine(Root, $"{game.Store}-{Convert.ToHexString(hash)[..10]}");
    }

    /// <returns>The new backup, or null if there was nothing to save (ReShade never installed).</returns>
    public BackupEntry? Create(GameInstallation game, string reason)
    {
        var files = new List<string>();
        if (File.Exists(game.ReShadeIniPath)) files.Add(game.ReShadeIniPath);

        var preset = IniFile.GetValue(game.ReShadeIniPath, "GENERAL", "PresetPath");
        var presetPath = ResolvePresetPath(game, preset);
        if (presetPath is not null && File.Exists(presetPath)) files.Add(presetPath);

        if (ReShadeService.FindReShadeDll(game.BinariesDirectory) is { } dll) files.Add(dll);

        if (files.Count == 0) return null;

        var created = DateTime.Now;
        var folder = Path.Combine(GameFolder(game), created.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);

        var manifest = new BackupManifest
        {
            Created = created,
            Reason = reason,
            Preset = presetPath is null ? null : Path.GetFileNameWithoutExtension(presetPath),
        };
        for (var i = 0; i < files.Count; i++)
        {
            var stored = $"{i}-{Path.GetFileName(files[i])}";
            File.Copy(files[i], Path.Combine(folder, stored), overwrite: true);
            manifest.Files.Add(new BackupFile { Original = files[i], Stored = stored });
        }
        File.WriteAllText(Path.Combine(folder, ManifestName), JsonSerializer.Serialize(manifest, JsonOptions));

        Prune(game);
        _log.Info($"Backup saved before {reason}: {files.Count} file(s).");
        return new BackupEntry(folder, created, reason, manifest.Preset);
    }

    /// <summary>The most recent backup of this installation, or null.</summary>
    public BackupEntry? Latest(GameInstallation game) => List(game).FirstOrDefault();

    /// <summary>
    /// Copies the saved files back where they were, then removes the backup, so the next "undo" goes
    /// one step further back.
    /// </summary>
    public void Restore(BackupEntry entry)
    {
        var manifest = ReadManifest(entry.Folder) ?? throw new InvalidDataException("The backup is damaged.");
        foreach (var file in manifest.Files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Original)!);
            File.Copy(Path.Combine(entry.Folder, file.Stored), file.Original, overwrite: true);
        }
        Directory.Delete(entry.Folder, recursive: true);
        _log.Success($"Restored the backup from {entry.Created:g} ({manifest.Files.Count} file(s)).");
    }

    private IEnumerable<BackupEntry> List(GameInstallation game)
    {
        var folder = GameFolder(game);
        if (!Directory.Exists(folder)) return Enumerable.Empty<BackupEntry>();

        return Directory.EnumerateDirectories(folder)
            .Select(dir => (Dir: dir, Manifest: ReadManifest(dir)))
            .Where(x => x.Manifest is not null)
            .Select(x => new BackupEntry(x.Dir, x.Manifest!.Created, x.Manifest.Reason, x.Manifest.Preset))
            .OrderByDescending(b => b.Created)
            .ToList();
    }

    private void Prune(GameInstallation game)
    {
        foreach (var old in List(game).Skip(Keep))
        {
            try { Directory.Delete(old.Folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* next time */ }
        }
    }

    private static BackupManifest? ReadManifest(string folder)
    {
        try
        {
            var path = Path.Combine(folder, ManifestName);
            return File.Exists(path) ? JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(path)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>PresetPath may be relative to the game's binaries folder.</summary>
    public static string? ResolvePresetPath(GameInstallation game, string? presetPath)
    {
        if (string.IsNullOrWhiteSpace(presetPath)) return null;
        try
        {
            return Path.GetFullPath(Path.IsPathRooted(presetPath) ? presetPath : Path.Combine(game.BinariesDirectory, presetPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

}

internal sealed class BackupManifest
{
    public DateTime Created { get; set; }
    public string Reason { get; set; } = "";
    public string? Preset { get; set; }
    public List<BackupFile> Files { get; set; } = new();
}

internal sealed class BackupFile
{
    public string Original { get; set; } = "";
    public string Stored { get; set; } = "";
}
