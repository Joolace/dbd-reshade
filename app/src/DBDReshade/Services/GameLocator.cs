using System.Text.Json;
using System.Text.RegularExpressions;
using DBDReshade.Models;
using Microsoft.Win32;

namespace DBDReshade.Services;

/// <summary>Finds Dead by Daylight installs from Steam (registry + library folders) and Epic Games.</summary>
public sealed partial class GameLocator
{
    private readonly LogService _log;

    public GameLocator(LogService log) => _log = log;

    public IReadOnlyList<GameInstallation> FindInstallations()
    {
        var found = new List<GameInstallation>();

        TryAdd(found, "Steam", FindSteamFromUninstallKey() ?? FindSteamFromLibraries());
        foreach (var path in FindEpic())
            TryAdd(found, "Epic", path);

        if (found.Count == 0)
            _log.Warning("Dead by Daylight installation not found (Steam/Epic).");
        else
            foreach (var g in found)
                _log.Info($"Found installation: {g.DisplayName}");

        return found;
    }

    /// <summary>Validates a folder the user picked by hand.</summary>
    public static GameInstallation? FromManualFolder(string folder)
    {
        // Accept the game root or any folder below it (Binaries, Win64…).
        var dir = new DirectoryInfo(folder);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "DeadByDaylight", "Binaries")))
            {
                var store = Directory.Exists(Path.Combine(dir.FullName, "DeadByDaylight", "Binaries", "EGS"))
                    ? "Epic" : "Steam";
                return new GameInstallation(store, dir.FullName);
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static void TryAdd(List<GameInstallation> list, string store, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        var full = Path.GetFullPath(path).TrimEnd('\\');
        if (list.Any(g => string.Equals(g.RootPath, full, StringComparison.OrdinalIgnoreCase))) return;

        list.Add(new GameInstallation(store, full));
    }

    private static string? FindSteamFromUninstallKey()
    {
        var subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {AppInfo.SteamAppId}";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hklm.OpenSubKey(subKey);
                if (key?.GetValue("InstallLocation") is string location && Directory.Exists(location))
                    return location;
            }
            catch
            {
                // Missing key or no access: try the next source.
            }
        }
        return null;
    }

    private static string? FindSteamFromLibraries()
    {
        try
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (steamKey?.GetValue("SteamPath") is not string steamPath) return null;

            steamPath = steamPath.Replace('/', '\\');
            var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) return null;

            var libraries = LibraryPathRegex().Matches(File.ReadAllText(vdf))
                .Select(m => m.Groups[1].Value.Replace(@"\\", @"\").Replace('/', '\\'))
                .Prepend(steamPath)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var library in libraries)
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppInfo.SteamAppId}.acf");
                if (!File.Exists(manifest)) continue;

                var match = InstallDirRegex().Match(File.ReadAllText(manifest));
                if (!match.Success) continue;

                var gamePath = Path.Combine(library, "steamapps", "common", match.Groups[1].Value);
                if (Directory.Exists(gamePath)) return gamePath;
            }
        }
        catch
        {
            // Unreadable Steam config: treat as not found.
        }
        return null;
    }

    private static IEnumerable<string> FindEpic()
    {
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) yield break;

        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            string? location = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (root.TryGetProperty("DisplayName", out var name)
                    && name.GetString()?.Contains("Dead by Daylight", StringComparison.OrdinalIgnoreCase) == true
                    && root.TryGetProperty("InstallLocation", out var loc))
                    location = loc.GetString();
            }
            catch
            {
                // Malformed manifest: skip it.
            }

            if (location is not null) yield return location;
        }
    }

    [GeneratedRegex("\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex("\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();
}
