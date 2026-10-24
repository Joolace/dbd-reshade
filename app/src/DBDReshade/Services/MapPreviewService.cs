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
/// <item>downloads the effects the preset needs into its own reshade-shaders folder (the game's are the fallback);</item>
/// <item>points that ReShade.ini at the chosen preset;</item>
/// <item>starts the copy in <c>--viewer</c> mode on the chosen map.</item>
/// </list>
/// </summary>
public sealed class MapPreviewService
{
    public const string ExeName = "DBDReshade Preview.exe";

    private readonly ReShadeService _reshade;
    private readonly ShaderService _shaders;
    private readonly LogService _log;

    public MapPreviewService(ReShadeService reshade, ShaderService shaders, LogService log)
    {
        _reshade = reshade;
        _shaders = shaders;
        _log = log;
    }

    private static string ViewerExe => Path.Combine(AppPaths.ViewerDirectory, ExeName);
    private static string ViewerIni => Path.Combine(AppPaths.ViewerDirectory, "ReShade.ini");

    private static bool IsViewerRunning =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)).Length > 0;

    public static bool IsReShadeInstalled => File.Exists(Path.Combine(AppPaths.ViewerDirectory, "dxgi.dll"));

    /// <summary>
    /// "Try on maps": shows a temporary copy of the preset in ReShade's performance mode, so trying a
    /// preset can never change the one in the library.
    /// </summary>
    /// <param name="status">Receives a line to show while effects download.</param>
    /// <returns>False if some effects couldn't be installed: the preview will run, but they won't show.</returns>
    public Task<bool> PrepareAsync(Preset preset, GameInstallation? game,
        IProgress<double>? reshadeProgress = null, IProgress<string>? status = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(AppPaths.ViewerDirectory);
        var copy = Path.Combine(AppPaths.ViewerDirectory, "preview-preset.ini");
        File.Copy(preset.FilePath, copy, overwrite: true);
        return PrepareCoreAsync(copy, game, editable: false, reshadeProgress, status, ct);
    }

    /// <summary>
    /// Preset editor: ReShade edits <paramref name="presetFile"/> directly (changes are saved as you go)
    /// and Print Screen saves screenshots to the editor's folder.
    /// </summary>
    public Task<bool> PrepareEditorAsync(string presetFile, GameInstallation? game,
        IProgress<double>? reshadeProgress = null, IProgress<string>? status = null, CancellationToken ct = default) =>
        PrepareCoreAsync(presetFile, game, editable: true, reshadeProgress, status, ct);

    private async Task<bool> PrepareCoreAsync(string presetFile, GameInstallation? game, bool editable,
        IProgress<double>? progress, IProgress<string>? status, CancellationToken ct)
    {
        await Task.Run(CopyViewer, ct);

        if (!IsReShadeInstalled)
        {
            _log.Info("Installing ReShade for the map preview…");
            await _reshade.InstallForExecutableAsync(ViewerExe, new System.Progress<double>(p => progress?.Report(p * 0.4)), ct);
        }
        else if (!IsViewerRunning)
        {
            // Keep the preview's ReShade as current as the game's, without asking: it's the app's own copy.
            try
            {
                if (await _reshade.CheckForUpdateAsync(AppPaths.ViewerDirectory, ct) is { } update)
                    await _reshade.UpdateAsync(ViewerExe, update.Url, new System.Progress<double>(p => progress?.Report(p * 0.4)), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warning($"Could not update the map preview's ReShade: {ex.Message}");
            }
        }
        progress?.Report(0.4);

        // The viewer has its own effects, downloaded as needed: the editor gets a broad set to choose from.
        bool? allEffects;
        try
        {
            var effectsProgress = new System.Progress<ShaderProgress>(p =>
            {
                if (p.PackageName.Length > 0) status?.Report(Loc.Format("Inst.Status.Effects", p.PackageName));
                progress?.Report(0.4 + 0.6 * (p.Index + p.Fraction) / Math.Max(1, p.Count));
            });
            if (editable)
            {
                await _shaders.EnsureEditorSetAsync(AppPaths.ViewerDirectory, ViewerIni, effectsProgress, ct);
                allEffects = (await _shaders.EnsureForPresetAsync(AppPaths.ViewerDirectory, ViewerIni, presetFile, effectsProgress, ct)).Count == 0;
            }
            else
            {
                allEffects = (await _shaders.EnsureForPresetAsync(AppPaths.ViewerDirectory, ViewerIni, presetFile, effectsProgress, ct)).Count == 0;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning($"Effects could not be downloaded for the map preview: {ex.Message}");
            allEffects = null; // fall back to the game's shaders
        }
        progress?.Report(1);

        return Configure(presetFile, game, editable, allEffects);
    }

    /// <summary>Starts the preview window; await <see cref="Process.WaitForExitAsync"/> to know when it closes.</summary>
    public Process Launch(string screenshotsFolder, string title)
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
        start.ArgumentList.Add(Loc.Get("Viewer.HelpHint"));
        start.ArgumentList.Add("--keys");
        start.ArgumentList.Add(KeyHelp());
        start.ArgumentList.Add("--theme");
        start.ArgumentList.Add(ThemeManager.IsDark ? "dark" : "light");

        var process = Process.Start(start) ?? throw new InvalidOperationException("The preview could not be started.");
        _log.Info($"Map preview started: {title}");
        return process;
    }

    /// <summary>The F1 key list, translated: one "key TAB action" per line.</summary>
    private static string KeyHelp() => string.Join("\n", new[]
    {
        ("← →", "Viewer.KeyImages"),
        ("F11", "Viewer.KeyFullScreen"),
        (Loc.Get("Viewer.KeyNameHome"), "Viewer.KeyReShade"),
        (Loc.Get("Viewer.KeyNamePrint"), "Viewer.KeyScreenshot"),
        ("F1", "Viewer.KeyHelp"),
        ("ESC", "Viewer.KeyClose"),
    }.Select(k => $"{k.Item1}\t{Loc.Get(k.Item2)}"));

    /// <summary>Copies the running app into the viewer folder when it's missing or out of date.</summary>
    private void CopyViewer()
    {
        Directory.CreateDirectory(AppPaths.ViewerDirectory);

        var source = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running exe.");
        var baseDir = AppContext.BaseDirectory;

        // A preview that's still open keeps its exe locked: reuse the copy we have.
        if (File.Exists(ViewerExe) && IsViewerRunning)
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

    /// <param name="ownEffects">True/false: the viewer's own effects are installed (all / not all of them);
    /// null: downloading them failed, so the game's shaders are used if it has any.</param>
    /// <returns>True if the preset's effects should all be available.</returns>
    private bool Configure(string presetFile, GameInstallation? game, bool editable, bool? ownEffects)
    {
        IniFile.SetValue(ViewerIni, "GENERAL", "PresetPath", presetFile);
        // Performance mode locks effect settings: right for trying a preset, wrong for editing one.
        IniFile.SetValue(ViewerIni, "GENERAL", "PerformanceMode", editable ? "0" : "1");
        // Skip ReShade's first-run tutorial: the user came to see the preset.
        IniFile.SetValue(ViewerIni, "OVERLAY", "TutorialProgress", "4");
        if (editable)
            IniFile.SetValue(ViewerIni, "SCREENSHOT", "SavePath", AppPaths.EditorScreenshotsDirectory);

        if (ownEffects is { } all)
        {
            // Only the viewer's own folders (an earlier version pointed at the game's), so no effect is found twice.
            IniFile.SetValue(ViewerIni, "GENERAL", "EffectSearchPaths", "");
            IniFile.SetValue(ViewerIni, "GENERAL", "TextureSearchPaths", "");
            ShaderService.EnsureSearchPaths(AppPaths.ViewerDirectory, ViewerIni);
            _log.Info($"Map preview preset: {Path.GetFileName(presetFile)}{(editable ? " (editor)" : "")}");
            return all;
        }

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

        _log.Info($"Map preview preset: {Path.GetFileName(presetFile)}{(editable ? " (editor)" : "")}");
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
