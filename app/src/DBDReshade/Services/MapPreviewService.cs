using System.Diagnostics;
using DBDReshade.Models;
using DBDReshade.Viewer;

namespace DBDReshade.Services;

/// <summary>
/// Shows a preset on real map screenshots without starting the game:
/// <list type="number">
/// <item>copies this exe to %LOCALAPPDATA%\DBDReshade\Viewer (ReShade must live next to the exe it hooks,
///       and must never hook the app's own UI);</item>
/// <item>installs ReShade there once;</item>
/// <item>points that ReShade.ini at the chosen preset and at the game's shaders, so the result matches the game;</item>
/// <item>starts the copy in <c>--viewer</c> mode on the chosen map.</item>
/// </list>
/// </summary>
public sealed class MapPreviewService
{
    public const string ExeName = "DBDReshade Preview.exe";

    private readonly ReShadeService _reshade;
    private readonly LogService _log;

    public MapPreviewService(ReShadeService reshade, LogService log)
    {
        _reshade = reshade;
        _log = log;
    }

    private static string ViewerExe => Path.Combine(AppPaths.ViewerDirectory, ExeName);
    private static string ViewerIni => Path.Combine(AppPaths.ViewerDirectory, "ReShade.ini");

    public static bool IsReShadeInstalled => File.Exists(Path.Combine(AppPaths.ViewerDirectory, "dxgi.dll"));

    /// <returns>False if no ReShade shaders were found: the preview will run, but effects may be missing.</returns>
    public async Task<bool> PrepareAsync(Preset preset, GameInstallation? game,
        IProgress<double>? reshadeProgress = null, CancellationToken ct = default)
    {
        await Task.Run(CopyViewer, ct);

        if (!IsReShadeInstalled)
        {
            _log.Info("Installing ReShade for the map preview…");
            await _reshade.InstallForExecutableAsync(ViewerExe, reshadeProgress, ct);
        }

        return Configure(preset, game);
    }

    public void Launch(string screenshotsFolder, string title, string hint)
    {
        var start = new ProcessStartInfo(ViewerExe)
        {
            UseShellExecute = false,
            WorkingDirectory = AppPaths.ViewerDirectory,
        };
        start.ArgumentList.Add(ViewerApp.Argument);
        start.ArgumentList.Add(screenshotsFolder);
        start.ArgumentList.Add("--title");
        start.ArgumentList.Add(title);
        start.ArgumentList.Add("--hint");
        start.ArgumentList.Add(hint);

        Process.Start(start);
        _log.Info($"Map preview started: {title}");
    }

    /// <summary>Copies the running app into the viewer folder when it's missing or out of date.</summary>
    private void CopyViewer()
    {
        Directory.CreateDirectory(AppPaths.ViewerDirectory);

        var source = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running exe.");
        var baseDir = AppContext.BaseDirectory;

        // A preview that's still open keeps its exe locked: reuse the copy we have.
        if (File.Exists(ViewerExe) && Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)).Length > 0)
            return;

        // Published single-file exe: one file. Debug build from Visual Studio: exe + DBDReshade.dll + dependencies.
        var isSingleFile = !File.Exists(Path.Combine(baseDir, "DBDReshade.dll"));
        if (!IsOutOfDate(source)) return;

        if (isSingleFile)
        {
            File.Copy(source, ViewerExe, overwrite: true);
        }
        else
        {
            foreach (var file in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(baseDir, file);
                var target = Path.Combine(AppPaths.ViewerDirectory,
                    string.Equals(file, source, StringComparison.OrdinalIgnoreCase) ? ExeName : relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }

        File.SetLastWriteTimeUtc(ViewerExe, File.GetLastWriteTimeUtc(source));
        _log.Info("Map preview viewer updated.");
    }

    private static bool IsOutOfDate(string source) =>
        !File.Exists(ViewerExe)
        || new FileInfo(ViewerExe).Length != new FileInfo(source).Length
        || File.GetLastWriteTimeUtc(ViewerExe) != File.GetLastWriteTimeUtc(source);

    /// <returns>True if shaders were found (the game's, or the viewer's own).</returns>
    private bool Configure(Preset preset, GameInstallation? game)
    {
        IniFile.SetValue(ViewerIni, "GENERAL", "PresetPath", preset.FilePath);
        // Skip ReShade's first-run tutorial: the user came to see the preset.
        IniFile.SetValue(ViewerIni, "OVERLAY", "TutorialProgress", "4");

        var shadersFound = false;
        if (game is not null)
        {
            var effects = GameSearchPaths(game, "EffectSearchPaths", @"reshade-shaders\Shaders");
            var textures = GameSearchPaths(game, "TextureSearchPaths", @"reshade-shaders\Textures");
            if (effects is not null)
            {
                IniFile.SetValue(ViewerIni, "GENERAL", "EffectSearchPaths", effects);
                if (textures is not null) IniFile.SetValue(ViewerIni, "GENERAL", "TextureSearchPaths", textures);
                shadersFound = true;
                _log.Info("Map preview uses the game's ReShade shaders.");
            }
        }

        if (!shadersFound)
            shadersFound = Directory.Exists(Path.Combine(AppPaths.ViewerDirectory, "reshade-shaders"));

        if (!shadersFound)
            _log.Warning("No ReShade shaders found for the map preview: some effects of the preset may not show.");

        _log.Info($"Map preview preset: {preset.FileName}");
        return shadersFound;
    }

    /// <summary>
    /// The game's shader folders as absolute paths (ReShade.ini stores them relative to the game exe),
    /// or the default reshade-shaders folder if the game has one. Null if the game has no shaders.
    /// </summary>
    private static string? GameSearchPaths(GameInstallation game, string key, string defaultFolder)
    {
        var bin = game.BinariesDirectory;
        var configured = IniFile.GetValue(game.ReShadeIniPath, "GENERAL", key);

        var paths = (configured ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Absolute(bin, p))
            .Where(p => Directory.Exists(p.TrimEnd('*').TrimEnd('\\')))
            .ToList();

        if (paths.Count == 0 && Directory.Exists(Path.Combine(bin, defaultFolder)))
            paths.Add(Path.Combine(bin, defaultFolder) + @"\**");

        return paths.Count == 0 ? null : string.Join(",", paths);
    }

    /// <summary>".\reshade-shaders\Shaders\**" → "C:\…\Win64\reshade-shaders\Shaders\**" (the ** wildcard is kept).</summary>
    private static string Absolute(string baseDir, string path)
    {
        var recursive = path.EndsWith("**", StringComparison.Ordinal);
        var folder = recursive ? path[..^2].TrimEnd('\\', '/') : path;
        var full = Path.IsPathRooted(folder) ? folder : Path.GetFullPath(Path.Combine(baseDir, folder));
        return recursive ? full + @"\**" : full;
    }
}
