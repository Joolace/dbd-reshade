using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DBDReshade.Services;

/// <summary>One entry of ReShade's EffectPackages.ini.</summary>
public sealed record EffectPackage(
    string Name, string InstallPath, string TextureInstallPath, string DownloadUrl,
    bool Required, IReadOnlySet<string> EffectFiles, IReadOnlySet<string> DenyEffectFiles);

public sealed record ShaderProgress(string PackageName, int Index, int Count, double Fraction);

/// <summary>
/// Installs ReShade's effect files (shaders). ReShade's setup only offers them in its interactive
/// "effect packages" page, which the unattended install skips, so without this a preset has nothing
/// to switch on.
///
/// Uses ReShade's own package list (crosire/reshade-shaders, branch "list"): each package names the
/// .fx files it contains, and presets name the .fx file of every effect ("Name@File.fx"), so only the
/// packages a preset needs are downloaded, into the same folders ReShade's setup uses.
/// </summary>
public sealed partial class ShaderService
{
    private const string PackageListUrl = "https://raw.githubusercontent.com/crosire/reshade-shaders/list/EffectPackages.ini";
    private const string EffectsFolder = @"reshade-shaders\Shaders";
    private const string TexturesFolder = @"reshade-shaders\Textures";

    /// <summary>Files presets use under an old or extra name that the package list doesn't mention.</summary>
    private static readonly Dictionary<string, string> KnownExtras = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SuperDepth3D_VR+.fx"] = "Depth3D by BlueSkyDefender",
        ["ComputeGravity.fx"] = "CobraFX by SirCobra",
    };

    /// <summary>What the preset editor offers: the packages the official presets use, plus qUINT.</summary>
    private static readonly string[] EditorPackages =
    {
        "SweetFX by CeeJay.dk", "Color effects by prod80", "Legacy effects", "AstrayFX by BlueSkyDefender",
        "Insane-Shaders by Lord of Lunacy", "fubax-shaders by Fubaxiusz", "qUINT by Marty McFly",
    };

    private readonly LogService _log;
    private IReadOnlyList<EffectPackage>? _packages;

    public ShaderService(LogService log) => _log = log;

    /// <summary>
    /// Makes sure every effect <paramref name="presetFile"/> uses is installed next to <paramref name="exeFolder"/>
    /// (the game's binaries folder, or the map preview's folder) and that ReShade.ini looks there.
    /// </summary>
    /// <returns>Effect files the preset uses that no official package provides (they stay missing).</returns>
    public async Task<IReadOnlyList<string>> EnsureForPresetAsync(string exeFolder, string reshadeIni, string presetFile,
        IProgress<ShaderProgress>? progress = null, CancellationToken ct = default)
    {
        var packages = await GetPackagesAsync(ct);
        var installed = InstalledEffects(exeFolder);
        var needed = EffectFilesOf(presetFile);

        var unknown = new List<string>();
        var toInstall = new List<EffectPackage>();
        foreach (var fx in needed.Where(fx => !installed.Contains(fx)))
        {
            var package = packages.FirstOrDefault(p => p.EffectFiles.Contains(fx))
                          ?? (KnownExtras.TryGetValue(fx, out var name) ? packages.FirstOrDefault(p => p.Name == name) : null);
            if (package is null) unknown.Add(fx);
            else if (!toInstall.Contains(package)) toInstall.Add(package);
        }

        // ReShade's "Required" package (DisplayDepth, UIMask…) always comes along, like in its own setup.
        foreach (var required in packages.Where(p => p.Required && !p.EffectFiles.Any(installed.Contains)))
            if (!toInstall.Contains(required)) toInstall.Insert(0, required);

        await InstallAsync(toInstall, exeFolder, progress, ct);
        EnsureSearchPaths(reshadeIni);

        if (unknown.Count > 0)
            _log.Warning($"Effects not in ReShade's package list (not installed): {string.Join(", ", unknown)}");
        return unknown;
    }

    /// <summary>A broad set of effects for creating presets from scratch in the editor.</summary>
    public async Task EnsureEditorSetAsync(string exeFolder, string reshadeIni,
        IProgress<ShaderProgress>? progress = null, CancellationToken ct = default)
    {
        var packages = await GetPackagesAsync(ct);
        var installed = InstalledEffects(exeFolder);

        var toInstall = packages
            .Where(p => p.Required || EditorPackages.Contains(p.Name))
            .Where(p => !p.EffectFiles.Any(installed.Contains))
            .ToList();

        await InstallAsync(toInstall, exeFolder, progress, ct);
        EnsureSearchPaths(reshadeIni);
    }

    /// <summary>The .fx files named in a preset's "Techniques=Name@File.fx,…" line.</summary>
    public static IReadOnlySet<string> EffectFilesOf(string presetFile)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(presetFile)) return files;

        foreach (var line in File.ReadLines(presetFile))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Techniques=", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var item in trimmed["Techniques=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var at = item.IndexOf('@');
                if (at >= 0) files.Add(item[(at + 1)..].Trim());
            }
        }
        return files;
    }

    // ---- Package list -------------------------------------------------------------------------

    private async Task<IReadOnlyList<EffectPackage>> GetPackagesAsync(CancellationToken ct)
    {
        if (_packages is not null) return _packages;

        string text;
        try
        {
            text = await Http.Client.GetStringAsync(PackageListUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning($"ReShade's effect list is unavailable, using the built-in copy: {ex.Message}");
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EffectPackages.ini")!;
            using var reader = new StreamReader(stream);
            text = await reader.ReadToEndAsync(ct);
        }

        _packages = ParsePackages(text);
        if (_packages.Count == 0) throw new InvalidDataException("ReShade's effect package list is empty.");
        return _packages;
    }

    private static IReadOnlyList<EffectPackage> ParsePackages(string ini)
    {
        var result = new List<EffectPackage>();
        foreach (var section in SectionRegex().Split(ini).Where(s => s.Contains("DownloadUrl=")))
        {
            var values = section.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Contains('='))
                .Select(l => (Key: l[..l.IndexOf('=')], Value: l[(l.IndexOf('=') + 1)..]))
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

            string Get(string key) => values.TryGetValue(key, out var v) ? v.Trim() : "";
            HashSet<string> Set(string key) => Get(key)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            result.Add(new EffectPackage(
                Get("PackageName"),
                Get("InstallPath") is { Length: > 0 } install ? install : @".\" + EffectsFolder,
                Get("TextureInstallPath") is { Length: > 0 } textures ? textures : @".\" + TexturesFolder,
                Get("DownloadUrl"),
                Get("Required") == "1",
                Set("EffectFiles"),
                Set("DenyEffectFiles")));
        }
        return result;
    }

    // ---- Installing ---------------------------------------------------------------------------

    private static HashSet<string> InstalledEffects(string exeFolder)
    {
        var folder = Path.Combine(exeFolder, EffectsFolder);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.fx", SearchOption.AllDirectories)
                .Select(f => Path.GetFileName(f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task InstallAsync(IReadOnlyList<EffectPackage> packages, string exeFolder,
        IProgress<ShaderProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.TempDirectory);
        for (var i = 0; i < packages.Count; i++)
        {
            var package = packages[i];
            var index = i;
            _log.Info($"Downloading effects: {package.Name}");
            progress?.Report(new ShaderProgress(package.Name, index, packages.Count, 0));

            var zip = Path.Combine(AppPaths.TempDirectory, $"effects-{i}.zip");
            try
            {
                await Http.DownloadFileAsync(package.DownloadUrl, zip,
                    new System.Progress<double>(p => progress?.Report(new ShaderProgress(package.Name, index, packages.Count, p))), ct);
                var count = await Task.Run(() => Extract(zip, package, exeFolder), ct);
                _log.Info($"Installed {count} effect file(s) from {package.Name}.");
            }
            finally
            {
                try { File.Delete(zip); } catch { /* temp file */ }
            }
        }
        if (packages.Count > 0) progress?.Report(new ShaderProgress("", packages.Count, packages.Count, 1));
    }

    /// <summary>
    /// Copies the archive's "Shaders" and "Textures" folders into the package's install paths, like ReShade's
    /// setup. Archives without a Shaders folder get their .fx/.fxh files copied as they are.
    /// </summary>
    private static int Extract(string zipPath, EffectPackage package, string exeFolder)
    {
        var effectsTarget = Path.GetFullPath(Path.Combine(exeFolder, package.InstallPath));
        var texturesTarget = Path.GetFullPath(Path.Combine(exeFolder, package.TextureInstallPath));

        using var archive = ZipFile.OpenRead(zipPath);
        var files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var hasShadersFolder = files.Any(e => FolderIndex(e.FullName, "Shaders") >= 0);

        var copied = 0;
        foreach (var entry in files)
        {
            if (package.DenyEffectFiles.Contains(entry.Name)) continue;

            var parts = entry.FullName.Split('/');
            string? target = null;

            var shaders = FolderIndex(entry.FullName, "Shaders");
            var textures = FolderIndex(entry.FullName, "Textures");
            if (shaders >= 0)
                target = Path.Combine(effectsTarget, Path.Combine(parts[(shaders + 1)..]));
            else if (textures >= 0)
                target = Path.Combine(texturesTarget, Path.Combine(parts[(textures + 1)..]));
            else if (!hasShadersFolder && IsShaderFile(entry.Name) && parts.Length > 1)
                target = Path.Combine(effectsTarget, Path.Combine(parts[1..])); // skip the "repo-branch/" root

            if (target is null) continue;

            // Never write outside the target folders, whatever the archive contains.
            var full = Path.GetFullPath(target);
            if (!full.StartsWith(effectsTarget, StringComparison.OrdinalIgnoreCase)
                && !full.StartsWith(texturesTarget, StringComparison.OrdinalIgnoreCase)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
            copied++;
        }
        return copied;
    }

    /// <summary>Index of a path segment named <paramref name="folder"/> (not the last one, which is the file).</summary>
    private static int FolderIndex(string path, string folder)
    {
        var parts = path.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals(folder, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static bool IsShaderFile(string name) =>
        name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".fxh", StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds the reshade-shaders folders to ReShade.ini's search paths if they're missing.</summary>
    private static void EnsureSearchPaths(string reshadeIni)
    {
        AddSearchPath(reshadeIni, "EffectSearchPaths", @".\" + EffectsFolder + @"\**");
        AddSearchPath(reshadeIni, "TextureSearchPaths", @".\" + TexturesFolder + @"\**");
    }

    private static void AddSearchPath(string ini, string key, string path)
    {
        var current = IniFile.GetValue(ini, "GENERAL", key) ?? "";
        var paths = current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var normalized = path.TrimEnd('*', '\\');
        if (paths.Any(p => p.TrimEnd('*', '\\').Equals(normalized, StringComparison.OrdinalIgnoreCase))) return;

        paths.Add(path);
        IniFile.SetValue(ini, "GENERAL", key, string.Join(",", paths));
    }

    [GeneratedRegex(@"^\[[^\]]+\]\s*$", RegexOptions.Multiline)]
    private static partial Regex SectionRegex();
}
