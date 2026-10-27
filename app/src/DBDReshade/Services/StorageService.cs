namespace DBDReshade.Services;

public sealed record StorageInfo(long PreviewsBytes, long MapsBytes, long LogsBytes, long TempBytes, long OfflineBytes)
{
    public long TotalBytes => PreviewsBytes + MapsBytes + LogsBytes + TempBytes + OfflineBytes;
}

/// <summary>
/// Measures and frees the disk space the app uses for things it can recreate:
/// cached previews (re-downloaded on demand), old logs and temporary downloads.
/// Presets and the user's own preview images are never deleted here.
/// </summary>
public sealed class StorageService
{
    private readonly PresetRepository _repository;
    private readonly LogService _log;

    public StorageService(PresetRepository repository, LogService log)
    {
        _repository = repository;
        _log = log;
    }

    public StorageInfo Measure() => new(
        CachedPreviews().Sum(Size),
        MapFiles().Sum(Size),
        OldLogs().Sum(Size),
        TempFiles().Sum(Size),
        OfflineService.OfflineBytes());

    /// <returns>Bytes freed.</returns>
    public long ClearPreviews() => DeleteAll(CachedPreviews(), "preview cache");

    /// <summary>Map screenshots and the preview copy of the app (both are recreated when the preview is used).</summary>
    public long ClearMaps()
    {
        var freed = DeleteAll(MapFiles(), "map preview files");
        foreach (var root in new[] { AppPaths.MapsDirectory, AppPaths.ViewerDirectory })
            DeleteEmptyFolders(root);
        return freed;
    }

    public long ClearLogs() => DeleteAll(OldLogs(), "old logs");

    public long ClearTemp() => DeleteAll(TempFiles(), "temporary files");

    /// <summary>ReShade's setup and the effect packages saved for offline use.</summary>
    public long ClearOffline()
    {
        var freed = DeleteAll(Files(AppPaths.OfflineDirectory, recursive: true), "offline resources");
        DeleteEmptyFolders(AppPaths.OfflineDirectory);
        return freed;
    }

    public long ClearAll() => ClearPreviews() + ClearMaps() + ClearLogs() + ClearTemp() + ClearOffline();

    private static IEnumerable<FileInfo> MapFiles() =>
        Files(AppPaths.MapsDirectory, recursive: true).Concat(Files(AppPaths.ViewerDirectory, recursive: true));

    private static void DeleteEmptyFolders(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length).ToList())
        {
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch (IOException) { /* in use */ }
            catch (UnauthorizedAccessException) { /* in use */ }
        }
    }

    /// <summary>Downloaded previews, excluding images that belong to the user's own presets.</summary>
    private IEnumerable<FileInfo> CachedPreviews()
    {
        var keep = _repository.UserPreviewFiles;
        return Files(AppPaths.PreviewsDirectory).Where(f => !keep.Contains(f.Name));
    }

    /// <summary>Every log except today's, which is still being written.</summary>
    private static IEnumerable<FileInfo> OldLogs() =>
        Files(AppPaths.LogsDirectory).Where(f => !string.Equals(f.FullName, AppPaths.LogFile, StringComparison.OrdinalIgnoreCase));

    /// <summary>ReShade setup downloads plus any interrupted download (*.part) left in the data folder.</summary>
    private static IEnumerable<FileInfo> TempFiles() =>
        Files(AppPaths.TempDirectory, recursive: true)
            .Concat(Files(AppPaths.DataRoot, recursive: true).Where(f => f.Extension == ".part"));

    private long DeleteAll(IEnumerable<FileInfo> files, string what)
    {
        long freed = 0;
        var failed = 0;
        foreach (var file in files.ToList())
        {
            try
            {
                var size = file.Length;
                file.Delete();
                freed += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++; // in use: skip it
            }
        }

        _log.Info($"Cleared {what}: {FormatBytes(freed)} freed{(failed > 0 ? $", {failed} file(s) in use were skipped" : "")}.");
        return freed;
    }

    private static IEnumerable<FileInfo> Files(string directory, bool recursive = false)
    {
        if (!Directory.Exists(directory)) return Enumerable.Empty<FileInfo>();
        try
        {
            return new DirectoryInfo(directory)
                .EnumerateFiles("*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Enumerable.Empty<FileInfo>();
        }
    }

    private static long Size(FileInfo file)
    {
        try { return file.Length; }
        catch (IOException) { return 0; }
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
