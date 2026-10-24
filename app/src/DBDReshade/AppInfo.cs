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
    public const string GitHubBranch = "main";

    public const string RepoUrl = "https://github.com/Joolace/dbd-reshade";
    public const string DiscordUrl = "https://discord.gg/mC7Eabu3QW";
    public const string InstagramUrl = "https://www.instagram.com/joolace";

    /// <summary>Folder of the repository holding the community translations (one JSON per language).</summary>
    public const string TranslationsFolder = "translations";

    /// <summary>Folder of the repository with the map screenshots, one subfolder per map (formerly D3Daylight).</summary>
    public const string MapsFolder = "media/realms";

    public const string TranslateGuideUrl = RepoUrl + "/blob/main/translations/README.md";

    /// <summary>Dead by Daylight Steam AppID.</summary>
    public const int SteamAppId = 381210;
}
