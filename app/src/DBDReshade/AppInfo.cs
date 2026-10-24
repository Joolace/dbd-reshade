using System.Reflection;

namespace DBDReshade;

/// <summary>Static application-wide constants.</summary>
public static class AppInfo
{
    public static Version Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(2, 0, 0);

    public static string VersionText => $"v{Version.ToString(3)}";

    public const string Author = "Joolace";

    public const string GitHubOwner = "Joolace";
    public const string GitHubRepo = "dbd-reshade";
    /// <summary>Branch of the new app: presets, translations and maps are read from here ("main" keeps the old PowerShell script).</summary>
    public const string DefaultBranch = "wpf-app";

    /// <summary>
    /// Branch actually used. Set the DBDRESHADE_BRANCH environment variable to test another branch
    /// (e.g. a pull request) without changing the code.
    /// </summary>
    public static string GitHubBranch { get; } =
        Environment.GetEnvironmentVariable("DBDRESHADE_BRANCH") is { Length: > 0 } branch ? branch.Trim() : DefaultBranch;

    public static bool IsTestBranch => GitHubBranch != DefaultBranch;

    public const string RepoUrl = "https://github.com/Joolace/dbd-reshade";
    public const string DiscordUrl = "https://discord.gg/mC7Eabu3QW";
    public const string InstagramUrl = "https://www.instagram.com/joolace";

    /// <summary>Folder of the repository holding the community translations (one JSON per language).</summary>
    public const string TranslationsFolder = "translations";

    /// <summary>Repository folder with the map screenshots, one subfolder per realm (formerly D3Daylight).</summary>
    public const string MapsFolder = "Maps";

    public const string TranslateGuideUrl = RepoUrl + "/blob/" + DefaultBranch + "/translations/README.md";

    /// <summary>Dead by Daylight Steam AppID.</summary>
    public const int SteamAppId = 381210;
}
