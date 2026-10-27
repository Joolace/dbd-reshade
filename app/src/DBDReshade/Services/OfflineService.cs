namespace DBDReshade.Services;

/// <summary>Where "Download everything" is: which step, and how far overall (0-1).</summary>
public sealed record OfflineProgress(string StepKey, string? Detail, double Overall);

/// <summary>
/// "Download everything for offline use" (Settings): presets, translations, every preview, every map's
/// screenshots, ReShade's setup and the effects the presets use. Afterwards installing presets, the previews
/// and "Try on maps" all work without internet. Anything already on disk is skipped, so running it again
/// only fetches what's new.
/// </summary>
public sealed class OfflineService
{
    private readonly PresetSyncService _sync;
    private readonly TranslationSyncService _translations;
    private readonly PresetRepository _repository;
    private readonly GitHubService _github;
    private readonly MapService _maps;
    private readonly ReShadeService _reshade;
    private readonly ShaderService _shaders;
    private readonly LogService _log;

    public OfflineService(PresetSyncService sync, TranslationSyncService translations, PresetRepository repository,
        GitHubService github, MapService maps, ReShadeService reshade, ShaderService shaders, LogService log)
    {
        _sync = sync;
        _translations = translations;
        _repository = repository;
        _github = github;
        _maps = maps;
        _reshade = reshade;
        _shaders = shaders;
        _log = log;
    }

    // Share of the progress bar each step takes (previews and maps are most of the download).
    private static readonly (string Key, double From, double To)[] Steps =
    {
        ("Offline.Step.Presets", 0.00, 0.05),
        ("Offline.Step.Previews", 0.05, 0.45),
        ("Offline.Step.Maps", 0.45, 0.80),
        ("Offline.Step.ReShade", 0.80, 0.88),
        ("Offline.Step.Effects", 0.88, 1.00),
    };

    /// <returns>How many items could not be downloaded (0 = everything is ready offline).</returns>
    public async Task<int> DownloadAllAsync(IProgress<OfflineProgress> progress, CancellationToken ct)
    {
        _log.Info("Downloading everything for offline use…");
        var failed = 0;

        void Report(int step, double fraction, string? detail = null)
        {
            var (key, from, to) = Steps[step];
            progress.Report(new OfflineProgress(key, detail, from + (to - from) * Math.Clamp(fraction, 0, 1)));
        }

        // 1. Presets, their descriptions and the translations.
        Report(0, 0);
        try
        {
            await _sync.SyncAsync(ct);
            await _translations.SyncAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed++;
            _log.Warning($"Offline download: preset sync failed: {ex.Message}");
        }

        // 2. Every preview image.
        var previews = _repository.Presets
            .Select(PresetRepository.GetPreviewPath).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !File.Exists(path))
            .ToList();
        var done = 0;
        Report(1, 0);
        await Parallel.ForEachAsync(previews, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (path, token) =>
            {
                try
                {
                    await _github.DownloadPreviewAsync(Path.GetFileName(path), path, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref failed);
                    _log.Warning($"Offline download: preview {Path.GetFileName(path)} failed: {ex.Message}");
                }
                var n = Interlocked.Increment(ref done);
                Report(1, (double)n / previews.Count, $"{n}/{previews.Count}");
            });

        // 3. Every map's screenshots for "Try on maps".
        Report(2, 0);
        try
        {
            var maps = (await _maps.GetMapsAsync(ct)).Where(m => !m.IsUser).ToList();
            for (var i = 0; i < maps.Count; i++)
            {
                var index = i;
                try
                {
                    await _maps.EnsureDownloadedAsync(maps[i],
                        new Progress<double>(p => Report(2, (index + p) / maps.Count, maps[index].Name)), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    _log.Warning($"Offline download: map {maps[i].Name} failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed++;
            _log.Warning($"Offline download: map list unavailable: {ex.Message}");
        }

        // 4. ReShade's setup, so it can be installed without reshade.me.
        Report(3, 0);
        try
        {
            await _reshade.DownloadSetupForOfflineAsync(new Progress<double>(p => Report(3, p)), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed++;
            _log.Warning($"Offline download: ReShade setup failed: {ex.Message}");
        }

        // 5. The effects every preset in the library uses (plus the editor's set).
        Report(4, 0);
        try
        {
            await _shaders.DownloadForOfflineAsync(_repository.Presets.Select(p => p.FilePath),
                new Progress<ShaderProgress>(p => Report(4, p.Count == 0 ? 1 : (p.Index + p.Fraction) / p.Count, p.PackageName)), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed++;
            _log.Warning($"Offline download: effects failed: {ex.Message}");
        }

        Report(4, 1);
        if (failed == 0) _log.Success("Everything is downloaded: the app works offline.");
        else _log.Warning($"Offline download finished, {failed} item(s) could not be downloaded.");
        return failed;
    }

    /// <summary>Size of what was saved only for offline use (ReShade setup + effect packages).</summary>
    public static long OfflineBytes()
    {
        if (!Directory.Exists(AppPaths.OfflineDirectory)) return 0;
        try
        {
            return new DirectoryInfo(AppPaths.OfflineDirectory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
