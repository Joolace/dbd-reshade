using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>Talks to the dbd-reshade GitHub repository: releases, preset list, metadata, previews.</summary>
public sealed class GitHubService
{
    private static string Api => $"https://api.github.com/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}";
    /// <summary>Contents API URL of a folder on the configured branch (without ?ref GitHub uses the default branch).</summary>
    private static string Contents(string folder) =>
        $"{Api}/contents/{folder}?ref={Uri.EscapeDataString(AppInfo.GitHubBranch)}";

    private static string Raw => $"https://raw.githubusercontent.com/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/{AppInfo.GitHubBranch}";

    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync($"{Api}/releases/latest", ct));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString();
        var url = root.GetProperty("html_url").GetString();
        if (tag is null || url is null) return null;

        // The files the build attaches to every release (see .github/workflows/build-app.yml).
        string? portable = null, setup = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var download = asset.GetProperty("browser_download_url").GetString();
                if (string.Equals(name, AppInfo.PortableAssetName, StringComparison.OrdinalIgnoreCase)) portable = download;
                else if (string.Equals(name, AppInfo.SetupAssetName, StringComparison.OrdinalIgnoreCase)) setup = download;
            }
        }

        return new ReleaseInfo(tag, url, ParseVersion(tag), portable, setup);
    }

    /// <summary>The .ini files in the repository's Presets folder.</summary>
    public Task<IReadOnlyList<RemoteFile>> ListPresetFilesAsync(CancellationToken ct = default) =>
        ListFilesAsync("Presets", name => name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase), ct);

    /// <summary>The images in media/preview-preset (used only to detect changed previews).</summary>
    public Task<IReadOnlyList<RemoteFile>> ListPreviewFilesAsync(CancellationToken ct = default) =>
        ListFilesAsync("media/preview-preset", IsImage, ct);

    /// <summary>Realm names: the subfolders of Maps/.</summary>
    public async Task<IReadOnlyList<string>> ListMapsAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync(Contents(AppInfo.MapsFolder), ct));
        return doc.RootElement.EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "dir")
            .Select(item => item.GetProperty("name").GetString()!)
            .ToList();
    }

    /// <summary>Screenshots of one realm (Maps/&lt;realm&gt;).</summary>
    public Task<IReadOnlyList<RemoteFile>> ListMapImagesAsync(string map, CancellationToken ct = default) =>
        ListFilesAsync($"{AppInfo.MapsFolder}/{Uri.EscapeDataString(map)}", IsImage, ct);

    /// <summary>The community translations (translations/*.json).</summary>
    public Task<IReadOnlyList<RemoteFile>> ListTranslationFilesAsync(CancellationToken ct = default) =>
        ListFilesAsync(AppInfo.TranslationsFolder, name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase), ct);

    private static async Task<IReadOnlyList<RemoteFile>> ListFilesAsync(
        string folder, Func<string, bool> filter, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync(Contents(folder), ct));

        var files = new List<RemoteFile>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            var type = item.GetProperty("type").GetString();
            var sha = item.TryGetProperty("sha", out var s) ? s.GetString() : null;
            var url = item.TryGetProperty("download_url", out var u) ? u.GetString() : null;

            if (type == "file" && name is not null && url is not null && filter(name))
                files.Add(new RemoteFile(name, url, sha ?? ""));
        }
        return files;
    }

    private static bool IsImage(string name) =>
        new[] { ".jpg", ".jpeg", ".png", ".gif" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    /// <summary>Raw text of media/presets.json.</summary>
    /// <remarks>
    /// raw.githubusercontent.com is a CDN that keeps files for about 5 minutes: right after a push it can still
    /// serve the old presets.json while the API already lists the new presets, which then show up without
    /// description or preview. The API always returns the current file, so it comes first; the raw address
    /// (with a cache-busting query) is only the fallback when the API limit of 60 requests an hour is reached.
    /// </remarks>
    public async Task<string> GetPresetMetadataJsonAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{Api}/contents/media/presets.json?ref={Uri.EscapeDataString(AppInfo.GitHubBranch)}");
            request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
            using var response = await Http.Client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            // Try the raw address below.
        }
        return await Http.Client.GetStringAsync($"{Raw}/media/presets.json?t={DateTime.UtcNow.Ticks}", ct);
    }

    /// <summary>Downloads one preview image. Previews are ~1 GB in total, so they're fetched on demand.</summary>
    public Task DownloadPreviewAsync(string fileName, string destination, CancellationToken ct = default) =>
        Http.DownloadFileAsync($"{Raw}/media/preview-preset/{Uri.EscapeDataString(fileName)}", destination, null, ct);

    /// <summary>Accepts "v1.4.2", "1.4.2", "DBDReshade-v1.4.2"…</summary>
    public static Version? ParseVersion(string tag)
    {
        var start = tag.IndexOfAny("0123456789".ToCharArray());
        if (start < 0) return null;

        var digits = new string(tag[start..].TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        if (!digits.Contains('.')) digits += ".0";
        return Version.TryParse(digits, out var v) ? v : null;
    }

    /// <summary>
    /// Same hash GitHub reports as "sha" for a file: SHA-1 of "blob {size}\0" + content.
    /// Lets the sync tell whether a local file differs from the repository without downloading it.
    /// </summary>
    public static string ComputeGitBlobSha(string path)
    {
        var content = File.ReadAllBytes(path);
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");

        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha1.AppendData(header);
        sha1.AppendData(content);
        return Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();
    }
}
