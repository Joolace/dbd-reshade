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

    /// <summary>
    /// Effects some older presets still list that their authors have since removed. None of them changes
    /// the image, so they're not reported as missing:
    /// SuperDepth3D_VR+.fx (Depth3D, merged into SuperDepth3D.fx in Feb 2025; presets only use its "Information" page),
    /// computeGravity.fx (CobraFX, replaced by Gravity.fx in Nov 2023, which those presets list too),
    /// FastFourierTransform.fx (Insane-Shaders developer test removed in 2023; its technique is "DoNothing").
    /// </summary>
    private static readonly HashSet<string> RetiredEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuperDepth3D_VR+.fx", "computeGravity.fx", "FastFourierTransform.fx",
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
    /// Makes sure every effect the given presets use is installed next to <paramref name="exeFolder"/>
    /// (the game's binaries folder, or the map preview's folder) and that ReShade.ini looks there.
    /// For the game this is called with the whole library, so switching preset from ReShade's own
    /// overlay works too, not only for the preset just installed.
    /// </summary>
    /// <returns>Effect files the presets use that are still missing afterwards (not in any official package).</returns>
    public async Task<IReadOnlyList<string>> EnsureForPresetsAsync(string exeFolder, string reshadeIni,
        IEnumerable<string> presetFiles, IProgress<ShaderProgress>? progress = null, CancellationToken ct = default)
    {
        var packages = await GetPackagesAsync(ct);
        var installed = InstalledEffects(exeFolder);
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presetFiles) needed.UnionWith(EffectFilesOf(preset));
        needed.ExceptWith(RetiredEffects);

        var toInstall = new List<EffectPackage>();
        foreach (var fx in needed.Where(fx => !installed.Contains(fx)))
        {
            var package = packages.FirstOrDefault(p => p.EffectFiles.Contains(fx));
            if (package is not null && !toInstall.Contains(package)) toInstall.Add(package);
        }

        // ReShade's "Required" package (ReShade.fxh, which every effect includes, DisplayDepth, UIMask…)
        // always comes along, like in its own setup.
        foreach (var required in packages.Where(p => p.Required && !p.EffectFiles.Any(installed.Contains)))
            if (!toInstall.Contains(required)) toInstall.Insert(0, required);
        if (!File.Exists(Path.Combine(exeFolder, EffectsFolder, "ReShade.fxh")))
            foreach (var required in packages.Where(p => p.Required))
                if (!toInstall.Contains(required)) toInstall.Insert(0, required);

        await InstallAsync(toInstall, exeFolder, progress, ct);
        EnsureSearchPaths(exeFolder, reshadeIni);

        // Check what's really there now, rather than trusting the package list.
        installed = InstalledEffects(exeFolder);
        var missing = needed.Where(fx => !installed.Contains(fx)).OrderBy(fx => fx).ToList();
        _log.Info($"Effects: {installed.Count} installed in {Path.Combine(exeFolder, EffectsFolder)}");
        if (missing.Count > 0)
            _log.Warning($"Effects not available in ReShade's official packages: {string.Join(", ", missing)}");
        return missing;
    }

    public Task<IReadOnlyList<string>> EnsureForPresetAsync(string exeFolder, string reshadeIni, string presetFile,
        IProgress<ShaderProgress>? progress = null, CancellationToken ct = default) =>
        EnsureForPresetsAsync(exeFolder, reshadeIni, new[] { presetFile }, progress, ct);

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
        EnsureSearchPaths(exeFolder, reshadeIni);
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

    /// <summary>
    /// Points ReShade.ini at the reshade-shaders folders, as full paths: ReShade then finds them whatever
    /// folder it considers its own. Other folders already listed are kept; entries for these same folders
    /// written another way (".\reshade-shaders\Shaders", without "**"…) are replaced, so no effect is loaded twice.
    /// </summary>
    public static void EnsureSearchPaths(string exeFolder, string reshadeIni)
    {
        SetSearchPath(reshadeIni, exeFolder, "EffectSearchPaths", Path.Combine(exeFolder, EffectsFolder));
        SetSearchPath(reshadeIni, exeFolder, "TextureSearchPaths", Path.Combine(exeFolder, TexturesFolder));
    }

    private static void SetSearchPath(string ini, string exeFolder, string key, string folder)
    {
        var target = Path.GetFullPath(folder).TrimEnd('\\');
        var paths = (IniFile.GetValue(ini, "GENERAL", key) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !SameFolder(p, exeFolder, target))
            .ToList();

        paths.Insert(0, target + @"\**");
        IniFile.SetValue(ini, "GENERAL", key, string.Join(",", paths));
    }

    private static bool SameFolder(string entry, string baseFolder, string target)
    {
        try
        {
            var folder = entry.Trim('"').TrimEnd('*').TrimEnd('\\', '/');
            if (folder.Length == 0) return false;
            var full = Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(baseFolder, folder));
            return string.Equals(full.TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // not a valid path: leave it alone
        }
    }

    [GeneratedRegex(@"^\[[^\]]+\]\s*$", RegexOptions.Multiline)]
    private static partial Regex SectionRegex();
}
