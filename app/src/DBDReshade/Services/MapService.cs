namespace DBDReshade.Services;

public sealed record MapInfo(string Name, bool IsDownloaded);

/// <summary>
/// Map screenshots for the preview (formerly bundled with D3Daylight, ~400 MB).
/// They live in Maps/&lt;realm&gt;/ on GitHub and are downloaded one realm at a time.
/// </summary>
public sealed class MapService
{
    private readonly GitHubService _github;
    private readonly LogService _log;

    public MapService(GitHubService github, LogService log)
    {
        _github = github;
        _log = log;
    }

    public static string GetFolder(string map) => Path.Combine(AppPaths.MapsDirectory, SafeName(map));

    /// <summary>Maps on GitHub, or only the downloaded ones when offline.</summary>
    public async Task<IReadOnlyList<MapInfo>> GetMapsAsync(CancellationToken ct = default)
    {
        IEnumerable<string> names;
        try
        {
            names = await _github.ListMapsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning($"Map list unavailable, showing downloaded maps only: {ex.Message}");
            names = Directory.Exists(AppPaths.MapsDirectory)
                ? Directory.EnumerateDirectories(AppPaths.MapsDirectory).Select(d => Path.GetFileName(d))
                : Enumerable.Empty<string>();
        }

        return names
            .Select(name => new MapInfo(name, HasImages(GetFolder(name))))
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Makes sure the map's screenshots are on disk and current (compared by git SHA).
    /// Works offline when they were downloaded before.
    /// </summary>
    /// <returns>The local folder with the screenshots.</returns>
    public async Task<string> EnsureDownloadedAsync(string map, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var folder = GetFolder(map);
        Directory.CreateDirectory(folder);

        IReadOnlyList<Models.RemoteFile> remote;
        try
        {
            remote = await _github.ListMapImagesAsync(map, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && HasImages(folder))
        {
            _log.Warning($"Using the downloaded screenshots of {map} (GitHub unavailable: {ex.Message})");
            return folder;
        }

        var todo = new List<Models.RemoteFile>();
        foreach (var file in remote)
        {
            var local = Path.Combine(folder, file.Name);
            if (!File.Exists(local)) { todo.Add(file); continue; }
            var sha = await Task.Run(() => GitHubService.ComputeGitBlobSha(local), ct);
            if (!string.Equals(sha, file.Sha, StringComparison.OrdinalIgnoreCase)) todo.Add(file);
        }

        for (var i = 0; i < todo.Count; i++)
        {
            var file = todo[i];
            var start = (double)i / todo.Count;
            var step = 1.0 / todo.Count;
            var fileProgress = progress is null ? null : new Progress<double>(p => progress.Report(start + p * step));
            await Http.DownloadFileAsync(file.DownloadUrl, Path.Combine(folder, file.Name), fileProgress, ct);
        }
        if (todo.Count > 0) _log.Info($"Downloaded {todo.Count} screenshot(s) of {map}.");

        // Screenshots removed on GitHub disappear here too, so the preview matches the repository.
        var names = remote.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in Directory.EnumerateFiles(folder).Where(f => !names.Contains(Path.GetFileName(f))).ToList())
            try { File.Delete(stale); } catch (IOException) { /* in use */ }

        progress?.Report(1);
        return folder;
    }

    private static bool HasImages(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any(IsImage);

    private static bool IsImage(string path) =>
        new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Map names are folder names on GitHub; strip anything Windows can't store just in case.</summary>
    private static string SafeName(string map) =>
        string.Concat(map.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
