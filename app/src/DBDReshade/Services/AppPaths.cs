using System.IO.Compression;
using System.Reflection;

namespace DBDReshade.Services;

/// <summary>
/// All writable data lives in %LOCALAPPDATA%\DBDReshade, so the app works the same
/// whether the exe sits in Downloads or in Program Files.
/// </summary>
public static class AppPaths
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DBDReshade");

    public static string PresetsDirectory => Path.Combine(DataRoot, "Presets");
    public static string PreviewsDirectory => Path.Combine(DataRoot, "Previews");
    public static string LogsDirectory => Path.Combine(DataRoot, "Logs");

    /// <summary>Map screenshots for the preview, downloaded one map at a time.</summary>
    public static string MapsDirectory => Path.Combine(DataRoot, "Maps");

    /// <summary>The user's own screenshots for the preview and the editor. Never deleted by the cleanup.</summary>
    public static string UserScreenshotsDirectory => Path.Combine(DataRoot, "My Screenshots");

    /// <summary>Presets being created in the editor (work in progress, never deleted by the cleanup).</summary>
    public static string EditorDirectory => Path.Combine(DataRoot, "Editor");

    /// <summary>Where ReShade saves screenshots taken in the editor (Print Screen).</summary>
    public static string EditorScreenshotsDirectory => Path.Combine(EditorDirectory, "Screenshots");

    /// <summary>Copy of this exe that runs the map preview, with its own ReShade install.</summary>
    public static string ViewerDirectory => Path.Combine(DataRoot, "Viewer");

    /// <summary>Translations downloaded from GitHub (override the ones built into the exe).</summary>
    public static string TranslationsDirectory => Path.Combine(DataRoot, "Translations");

    /// <summary>"Download everything for offline use": ReShade's setup and the effect packages, kept for installs without internet.</summary>
    public static string OfflineDirectory => Path.Combine(DataRoot, "Offline");
    public static string OfflineEffectsDirectory => Path.Combine(OfflineDirectory, "Effects");
    public static string OfflineEffectListFile => Path.Combine(OfflineDirectory, "EffectPackages.ini");

    /// <summary>Downloads that are only needed for a moment (the ReShade setup).</summary>
    public static string TempDirectory => Path.Combine(Path.GetTempPath(), "DBDReshade");

    /// <summary>Metadata mirrored from GitHub (media/presets.json). Overwritten on every sync.</summary>
    public static string RemoteMetadataFile => Path.Combine(DataRoot, "presets.json");

    /// <summary>Metadata for presets the user added themselves. Never touched by the sync.</summary>
    public static string UserMetadataFile => Path.Combine(DataRoot, "user-presets.json");

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    public static string LogFile => Path.Combine(LogsDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.txt");

    /// <summary>Creates the data folders and unpacks the bundled presets on first run.</summary>
    public static void Initialize()
    {
        Directory.CreateDirectory(PresetsDirectory);
        Directory.CreateDirectory(PreviewsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(TranslationsDirectory);
        Directory.CreateDirectory(MapsDirectory);
        Directory.CreateDirectory(UserScreenshotsDirectory);
        Directory.CreateDirectory(EditorScreenshotsDirectory);

        var firstRun = !Directory.EnumerateFiles(PresetsDirectory, "*.ini").Any();
        if (firstRun || !File.Exists(RemoteMetadataFile))
            ExtractSeed(overwrite: false);
    }

    private static void ExtractSeed(bool overwrite)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("seed.zip");
        if (stream is null) return;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

            var destination = Path.GetFullPath(Path.Combine(DataRoot, entry.FullName));
            if (!destination.StartsWith(DataRoot, StringComparison.OrdinalIgnoreCase)) continue;
            if (!overwrite && File.Exists(destination)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }
}
