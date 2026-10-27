using System.Text.Json;
using System.Text.Json.Serialization;

namespace DBDReshade.Services;

/// <summary>One GitHub release as shown in the "What's new" window.</summary>
public sealed record ReleaseNotes(string Tag, string Name, DateTimeOffset Published, string Body, string Url)
{
    [JsonIgnore]
    public Version? Version => GitHubService.ParseVersion(Tag);
}

/// <summary>
/// The changelog shown in "What's new": the notes of the latest GitHub releases. A copy is kept on disk so
/// the window still shows something when GitHub can't be reached (offline, or the 60-requests-an-hour limit).
/// </summary>
public sealed class ChangelogService
{
    private const int MaxReleases = 15;
    private static string CacheFile => Path.Combine(AppPaths.DataRoot, "changelog.json");

    private readonly LogService _log;

    public ChangelogService(LogService log) => _log = log;

    /// <summary>The releases, newest first: from GitHub, or from the local copy when GitHub can't be reached.</summary>
    public async Task<IReadOnlyList<ReleaseNotes>> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            var url = $"https://api.github.com/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases?per_page={MaxReleases}";
            var json = await Http.Client.GetStringAsync(url, timeout.Token);
            var releases = Parse(json);
            if (releases.Count > 0)
            {
                try { File.WriteAllText(CacheFile, JsonSerializer.Serialize(releases)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* non-critical */ }
            }
            return releases;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.Info($"Changelog from GitHub unavailable ({ex.Message}), using the saved copy.");
            return ReadCache();
        }
    }

    private static List<ReleaseNotes> Parse(string json)
    {
        var list = new List<ReleaseNotes>();
        foreach (var item in JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? new())
        {
            if (item.Draft || item.Prerelease || string.IsNullOrWhiteSpace(item.Tag)) continue;
            list.Add(new ReleaseNotes(
                item.Tag!,
                string.IsNullOrWhiteSpace(item.Name) ? item.Tag! : item.Name!,
                item.Published ?? DateTimeOffset.MinValue,
                item.Body ?? "",
                item.Url ?? AppInfo.ReleasesUrl));
        }
        return list;
    }

    private static IReadOnlyList<ReleaseNotes> ReadCache()
    {
        try
        {
            return File.Exists(CacheFile)
                ? JsonSerializer.Deserialize<List<ReleaseNotes>>(File.ReadAllText(CacheFile)) ?? new()
                : new List<ReleaseNotes>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new List<ReleaseNotes>();
        }
    }
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? Tag { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("html_url")] public string? Url { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? Published { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
}
