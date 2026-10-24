using System.Diagnostics;
using System.Text.Json;
using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>
/// Starts Dead by Daylight through its store, exactly like the "Play" button there: the store link
/// opens Steam/Epic if they are closed (and asks to log in if needed), then starts the game, with
/// Easy Anti-Cheat, updates and cloud saves handled as usual. The Xbox version is started as an app
/// (like from the Start menu), or through its gamelaunchhelper.exe if the app can't be identified.
/// </summary>
public sealed class GameLauncher
{
    private readonly LogService _log;

    public GameLauncher(LogService log) => _log = log;

    public void Launch(GameInstallation game)
    {
        var uri = game.Store switch
        {
            "Epic" => EpicUri(game) ?? throw new InvalidOperationException("Epic Games manifest for this installation not found."),
            "Xbox" => XboxUri(game),
            _ => $"steam://rungameid/{AppInfo.SteamAppId}",
        };

        _log.Info($"Starting Dead by Daylight ({game.Store}): {uri}");

        // The app runs as administrator; opening the link through Explorer hands it to the normal,
        // non-elevated desktop, so Steam/Epic and the game don't start as administrator.
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { uri }, UseShellExecute = false });
    }

    /// <summary>
    /// "shell:AppsFolder\&lt;package family&gt;!&lt;app id&gt;", the same thing the Start menu tile runs.
    /// The package is the one Windows registered for this Content folder; the app id comes from the
    /// game's MicrosoftGame.config. Falls back to gamelaunchhelper.exe itself.
    /// </summary>
    private string XboxUri(GameInstallation game)
    {
        try
        {
            const string repository = @"Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
            using var packages = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(repository);
            foreach (var fullName in packages?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var package = packages!.OpenSubKey(fullName);
                if (package?.GetValue("PackageRootFolder") is not string root
                    || !string.Equals(Path.GetFullPath(root).TrimEnd('\\'), game.RootPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Full name: Name_Version_Arch_ResourceId_PublisherId → family: Name_PublisherId
                var parts = fullName.Split('_');
                if (parts.Length < 5) break;
                return $"shell:AppsFolder\\{parts[0]}_{parts[^1]}!{XboxAppId(game)}";
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Xbox app package not found: {ex.Message}");
        }

        return game.GameExecutable
            ?? throw new FileNotFoundException($"{GameInstallation.XboxLauncher} not found in {game.RootPath}");
    }

    /// <summary>The Id of the game's executable in MicrosoftGame.config (usually "Game").</summary>
    private static string XboxAppId(GameInstallation game)
    {
        try
        {
            var config = System.Xml.Linq.XDocument.Load(Path.Combine(game.RootPath, "MicrosoftGame.config"));
            var executable = config.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable");
            return executable?.Attribute("Id")?.Value is { Length: > 0 } id ? id : "Game";
        }
        catch
        {
            return "Game";
        }
    }

    /// <summary>
    /// The Epic launcher link for this install, built from its manifest
    /// (com.epicgames.launcher://apps/namespace:item:app?action=launch&amp;silent=true).
    /// </summary>
    private string? EpicUri(GameInstallation game)
    {
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return null;

        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var location = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                if (location is null
                    || !string.Equals(Path.GetFullPath(location).TrimEnd('\\'), game.RootPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var ns = root.GetProperty("CatalogNamespace").GetString();
                var item = root.GetProperty("CatalogItemId").GetString();
                var app = root.GetProperty("AppName").GetString();
                return $"com.epicgames.launcher://apps/{ns}%3A{item}%3A{app}?action=launch&silent=true";
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
            {
                // Malformed manifest: try the next one.
            }
        }

        return null;
    }
}
