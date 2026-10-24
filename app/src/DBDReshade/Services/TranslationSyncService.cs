namespace DBDReshade.Services;

/// <summary>
/// Downloads translations/*.json from the GitHub repository, so translation fixes and new
/// languages reach users without an app update. Files are compared by git SHA, and a file is
/// only saved if it parses; a broken translation never replaces a working one.
/// </summary>
public sealed class TranslationSyncService
{
    private readonly GitHubService _github;
    private readonly LogService _log;

    public TranslationSyncService(GitHubService github, LogService log)
    {
        _github = github;
        _log = log;
    }

    /// <returns>Number of translation files added or updated.</returns>
    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        var remote = await _github.ListTranslationFilesAsync(ct);
        var changed = 0;

        foreach (var file in remote)
        {
            var local = Path.Combine(AppPaths.TranslationsDirectory, file.Name);
            if (File.Exists(local) && !string.IsNullOrEmpty(file.Sha)
                && await Task.Run(() => GitHubService.ComputeGitBlobSha(local), ct) == file.Sha)
                continue;

            try
            {
                // Bytes, not a string: the saved file must hash exactly like the one on GitHub.
                var bytes = await Http.Client.GetByteArrayAsync(file.DownloadUrl, ct);
                using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
                var text = await reader.ReadToEndAsync(ct);

                if (!Loc.TryParse(text, out _))
                {
                    _log.Warning($"Translation {file.Name} on GitHub is invalid and was skipped.");
                    continue;
                }

                await File.WriteAllBytesAsync(local, bytes, ct);
                changed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warning($"Could not download translation {file.Name}: {ex.Message}");
            }
        }

        if (changed > 0)
        {
            Loc.Reload();
            _log.Info($"Translations updated from GitHub: {changed} file(s).");
        }
        return changed;
    }
}
