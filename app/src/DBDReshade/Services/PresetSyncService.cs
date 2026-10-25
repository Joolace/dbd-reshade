using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>
/// Brings the local library up to date with the GitHub repository (branch wpf-app, see AppInfo.GitHubBranch), without an app update:
/// <list type="bullet">
/// <item>media/presets.json replaces the local metadata (descriptions, sources, preview names);</item>
/// <item>new .ini files in Presets/ are downloaded, changed ones re-downloaded (compared by git SHA);</item>
/// <item>cached previews whose image changed on GitHub are dropped, so they're fetched again on demand.</item>
/// </list>
/// The user's own presets and the ones they deleted are never touched.
/// </summary>
public sealed class PresetSyncService
{
    private readonly GitHubService _github;
    private readonly PresetRepository _repository;
    private readonly LogService _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PresetSyncService(GitHubService github, PresetRepository repository, LogService log)
    {
        _github = github;
        _repository = repository;
        _log = log;
    }

    /// <summary>Raised after every successful sync (on the caller's thread).</summary>
    public event EventHandler<SyncResult>? Synced;

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _log.Info("Checking GitHub for new or updated presets…");

            await UpdateMetadataAsync(ct);

            var (added, updated) = await SyncPresetFilesAsync(ct);
            var refreshed = await DropOutdatedPreviewsAsync(ct);

            if (added.Count > 0) _repository.MarkNew(added);
            _repository.Reload();

            var result = new SyncResult(added.Count, updated, refreshed);
            _log.Success(result.ChangedPresets > 0 || refreshed > 0
                ? $"Presets synced: {added.Count} new, {updated} updated, {refreshed} preview(s) refreshed."
                : "Presets are already up to date.");
            Synced?.Invoke(this, result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// A broken presets.json must not block new presets: keep the previous copy and carry on.
    /// (Network errors still propagate, since nothing else would work either.)
    /// </summary>
    private async Task UpdateMetadataAsync(CancellationToken ct)
    {
        var json = await _github.GetPresetMetadataJsonAsync(ct);
        try
        {
            _repository.SaveRemoteMetadata(json);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
        {
            _log.Warning($"presets.json on GitHub is invalid, keeping the previous descriptions: {ex.Message}");
        }
    }

    private async Task<(List<string> Added, int Updated)> SyncPresetFilesAsync(CancellationToken ct)
    {
        var remote = await _github.ListPresetFilesAsync(ct);
        var added = new List<string>();
        var updated = 0;

        foreach (var file in remote)
        {
            if (_repository.IsIgnored(file.Name) || _repository.IsCustom(file.Name)) continue;

            var local = Path.Combine(AppPaths.PresetsDirectory, file.Name);
            var exists = File.Exists(local);
            if (exists && await IsSameAsync(local, file.Sha, ct)) continue;

            try
            {
                await Http.DownloadFileAsync(file.DownloadUrl, local, null, ct);
                if (exists) updated++; else added.Add(file.Name);
                _log.Info($"{(exists ? "Updated" : "Downloaded")} preset {file.Name}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error($"Could not download {file.Name}: {ex.Message}");
            }
        }
        return (added, updated);
    }

    /// <summary>Deletes cached previews that differ from GitHub; they are re-downloaded when next shown.</summary>
    private async Task<int> DropOutdatedPreviewsAsync(CancellationToken ct)
    {
        IReadOnlyList<RemoteFile> remote;
        try
        {
            remote = await _github.ListPreviewFilesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning($"Could not check previews for changes: {ex.Message}");
            return 0;
        }

        var keep = _repository.UserPreviewFiles;
        var dropped = 0;
        foreach (var file in remote)
        {
            if (keep.Contains(file.Name)) continue;

            var local = Path.Combine(AppPaths.PreviewsDirectory, file.Name);
            if (!File.Exists(local) || await IsSameAsync(local, file.Sha, ct)) continue;

            try
            {
                File.Delete(local);
                dropped++;
            }
            catch (IOException)
            {
                // In use right now: it'll be checked again next sync.
            }
        }
        return dropped;
    }

    private static async Task<bool> IsSameAsync(string localPath, string remoteSha, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(remoteSha)) return true; // can't tell: keep what we have
        var localSha = await Task.Run(() => GitHubService.ComputeGitBlobSha(localPath), ct);
        return string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase);
    }
}
