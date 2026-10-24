using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>Detects, downloads and installs ReShade, and points ReShade.ini at the chosen preset.</summary>
public sealed partial class ReShadeService
{
    private const string ReShadeSite = "https://reshade.me";
    private readonly LogService _log;

    public ReShadeService(LogService log) => _log = log;

    /// <summary>True while the game is running: ReShade's files are in use and it would overwrite ReShade.ini on exit.</summary>
    public static bool IsGameRunning(GameInstallation game) =>
        game.GameExecutable is { } exe
        && Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Length > 0;

    public bool IsInstalled(GameInstallation game) =>
        File.Exists(game.ReShadeIniPath)
        || File.Exists(Path.Combine(game.BinariesDirectory, "dxgi.dll"));

    /// <summary>Finds the newest "ReShade_Setup_X.Y.Z.exe" (not the Addon build) linked from reshade.me.</summary>
    public async Task<string> GetLatestSetupUrlAsync(CancellationToken ct = default) =>
        (await GetLatestAsync(ct)).Url;

    /// <summary>The newest ReShade on reshade.me: its version and setup download link.</summary>
    public async Task<(Version Version, string Url)> GetLatestAsync(CancellationToken ct = default)
    {
        var html = await Http.Client.GetStringAsync(ReShadeSite + "/", ct);

        var best = SetupLinkRegex().Matches(html)
            .Select(m => (Href: m.Groups["href"].Value, Version: Version.TryParse(m.Groups["ver"].Value, out var v) ? v : null))
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        if (best.Href is null)
            throw new InvalidOperationException("Could not find the ReShade download link on reshade.me.");

        var url = best.Href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? best.Href
            : ReShadeSite + (best.Href.StartsWith('/') ? "" : "/") + best.Href;
        return (best.Version!, url);
    }

    /// <summary>
    /// Version of the ReShade installed in <paramref name="folder"/> (read from its dxgi.dll),
    /// or null if there's none or the dxgi.dll there isn't ReShade's.
    /// </summary>
    public static Version? GetInstalledVersion(string folder)
    {
        var dll = Path.Combine(folder, "dxgi.dll");
        if (!File.Exists(dll)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dll);
            if (info.ProductName?.Contains("ReShade", StringComparison.OrdinalIgnoreCase) != true
                && info.FileDescription?.Contains("ReShade", StringComparison.OrdinalIgnoreCase) != true) return null;
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The newest version, if it's newer than the one installed in <paramref name="folder"/>; else null.</summary>
    public async Task<(Version Installed, Version Latest, string Url)?> CheckForUpdateAsync(string folder, CancellationToken ct = default)
    {
        var installed = GetInstalledVersion(folder);
        if (installed is null) return null;

        var (latest, url) = await GetLatestAsync(ct);
        var latest3 = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
        return latest3 > installed ? (installed, latest3, url) : null;
    }

    /// <summary>
    /// Updates the ReShade next to <paramref name="exe"/>, keeping ReShade.ini, presets and effects.
    /// The setup carries the ReShade DLLs as a zip archive: the new ReShade64.dll simply replaces dxgi.dll.
    /// If that archive can't be read, the setup is run unattended again, which updates it as well.
    /// </summary>
    public async Task UpdateAsync(string exe, string setupUrl, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var folder = Path.GetDirectoryName(exe)!;
        var tempDir = AppPaths.TempDirectory;
        Directory.CreateDirectory(tempDir);
        var setup = Path.Combine(tempDir, Path.GetFileName(new Uri(setupUrl).LocalPath));

        try
        {
            _log.Info($"Updating ReShade: downloading {setupUrl}");
            await Http.DownloadFileAsync(setupUrl, setup, progress, ct);

            if (await Task.Run(() => TryReplaceDll(setup, folder), ct))
            {
                _log.Success($"ReShade updated to {GetInstalledVersion(folder)}.");
                return;
            }

            _log.Info("Running ReShade setup to update…");
            await RunSetupAsync(setup, exe, ct);
            _log.Success("ReShade updated.");
        }
        finally
        {
            try { File.Delete(setup); } catch { /* leftover temp file is harmless */ }
        }
    }

    private bool TryReplaceDll(string setup, string folder)
    {
        try
        {
            using var archive = ZipFile.OpenRead(setup);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("ReShade64.dll", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return false;

            var target = Path.Combine(folder, "dxgi.dll");
            var temp = target + ".new";
            entry.ExtractToFile(temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (InvalidDataException)
        {
            return false; // not readable as a zip: use the setup instead
        }
    }

    /// <summary>Downloads the latest setup and runs it unattended against the game executable.</summary>
    public Task InstallAsync(GameInstallation game, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var exe = game.GameExecutable
            ?? throw new FileNotFoundException($"Game executable (*-Shipping.exe) not found in {game.BinariesDirectory}");
        return InstallForExecutableAsync(exe, progress, ct);
    }

    /// <summary>
    /// Installs ReShade (DXGI) next to any executable: the game, or the map preview copy of this app.
    /// </summary>
    public async Task InstallForExecutableAsync(string exe, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        _log.Info("Fetching the latest version of ReShade…");
        var url = await GetLatestSetupUrlAsync(ct);

        var tempDir = AppPaths.TempDirectory;
        Directory.CreateDirectory(tempDir);
        var setup = Path.Combine(tempDir, Path.GetFileName(new Uri(url).LocalPath));

        try
        {
            _log.Info($"Downloading {url}");
            await Http.DownloadFileAsync(url, setup, progress, ct);

            _log.Info("Running ReShade setup…");
            await RunSetupAsync(setup, exe, ct);
            _log.Success("ReShade installed.");
        }
        finally
        {
            try { File.Delete(setup); } catch { /* leftover temp file is harmless */ }
        }
    }

    private static async Task RunSetupAsync(string setup, string exe, CancellationToken ct)
    {
        // ReShade setup CLI: <target exe> --api <api> --headless
        // dxgi covers DirectX 10/11/12, which is what Dead by Daylight uses.
        var startInfo = new ProcessStartInfo(setup)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(exe);
        startInfo.ArgumentList.Add("--api");
        startInfo.ArgumentList.Add("dxgi");
        startInfo.ArgumentList.Add("--headless");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the ReShade setup.");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ReShade setup exited with code {process.ExitCode}.");
    }

    /// <summary>Sets [GENERAL] PresetPath in ReShade.ini, creating the file/section if needed.</summary>
    public void SetPresetPath(GameInstallation game, string presetFile)
    {
        IniFile.SetValue(game.ReShadeIniPath, "GENERAL", "PresetPath", presetFile);
        _log.Info($"ReShade.ini → PresetPath={presetFile}");
    }

    /// <summary>Reads the preset currently configured in ReShade.ini, if any.</summary>
    public string? GetCurrentPresetPath(GameInstallation game) =>
        IniFile.GetValue(game.ReShadeIniPath, "GENERAL", "PresetPath");

    // Matches href=".../ReShade_Setup_6.4.1.exe"; the Addon build ends in _Addon.exe so it is excluded.
    [GeneratedRegex("href=\"(?<href>[^\"]*ReShade_Setup_(?<ver>\\d+(?:\\.\\d+)+)\\.exe)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SetupLinkRegex();
}

/// <summary>Minimal INI editor that preserves every other line of the file untouched.</summary>
public static class IniFile
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public static string? GetValue(string path, string section, string key)
    {
        if (!File.Exists(path)) return null;

        var inSection = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (IsSection(line)) { inSection = SectionName(line).Equals(section, StringComparison.OrdinalIgnoreCase); continue; }
            if (inSection && TrySplit(line, out var k, out var v) && k.Equals(key, StringComparison.OrdinalIgnoreCase))
                return v;
        }
        return null;
    }

    public static void SetValue(string path, string section, string key, string value)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var newLine = $"{key}={value}";

        var sectionIndex = lines.FindIndex(l => IsSection(l.Trim())
            && SectionName(l.Trim()).Equals(section, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex < 0)
        {
            // Put the section at the top, like ReShade does.
            lines.InsertRange(0, new[] { $"[{section}]", newLine, "" });
        }
        else
        {
            var replaced = false;
            for (var i = sectionIndex + 1; i < lines.Count && !IsSection(lines[i].Trim()); i++)
            {
                if (TrySplit(lines[i].Trim(), out var k, out _) && k.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = newLine;
                    replaced = true;
                    break;
                }
            }
            if (!replaced) lines.Insert(sectionIndex + 1, newLine);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines, Utf8NoBom);
    }

    private static bool IsSection(string line) => line.StartsWith('[') && line.EndsWith(']');
    private static string SectionName(string line) => line[1..^1].Trim();

    private static bool TrySplit(string line, out string key, out string value)
    {
        var eq = line.IndexOf('=');
        if (eq <= 0 || line.StartsWith(';') || line.StartsWith('#'))
        {
            key = value = "";
            return false;
        }
        key = line[..eq].Trim();
        value = line[(eq + 1)..].Trim();
        return true;
    }
}
