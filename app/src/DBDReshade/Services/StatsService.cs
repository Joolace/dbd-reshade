using System.Net.Http.Json;
using System.Text.Json;

namespace DBDReshade.Services;

/// <summary>
/// Community ranking: tells the stats service (stats-worker/, a Cloudflare Worker) when an official preset
/// is installed, and downloads how many times each preset was installed. Fully anonymous: the only thing
/// sent is the preset's file name and a random id of this installation, and users can turn it off in
/// Settings. Everything here is best effort: when the service is unreachable the app just works without it.
/// </summary>
public sealed class StatsService
{
    /// <summary>How long the downloaded ranking is reused before asking the service again.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);

    private static string CacheFile => Path.Combine(AppPaths.DataRoot, "ranking.json");

    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public StatsService(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"DBDReshade/{AppInfo.Version.ToString(3)}");
    }

    /// <summary>The ranking exists (the service address is set in AppInfo).</summary>
    public static bool IsAvailable => !string.IsNullOrWhiteSpace(AppInfo.StatsUrl);

    /// <summary>Installs per preset file name (empty until loaded, or if the ranking is off).</summary>
    public IReadOnlyDictionary<string, int> Counts { get; private set; } = new Dictionary<string, int>();

    public event EventHandler? Updated;

    /// <summary>
    /// Loads the ranking: from the local copy if recent (and not empty), otherwise from the service.
    /// <paramref name="force"/> skips the local copy (used right after an install, to show it at once).
    /// </summary>
    public async Task LoadAsync(bool force = false, CancellationToken ct = default)
    {
        if (!IsAvailable) return;

        var cached = force ? null : ReadCache();
        if (cached is not null)
        {
            Counts = cached.Value.Counts;
            Updated?.Invoke(this, EventArgs.Empty);
            if (Counts.Count > 0 && DateTime.UtcNow - cached.Value.SavedUtc < CacheFor) return;
        }

        try
        {
            var top = await _http.GetFromJsonAsync<TopResponse>($"{AppInfo.StatsUrl.TrimEnd('/')}/v1/top", ct);
            if (top?.Presets is null) return;

            Counts = new Dictionary<string, int>(top.Presets, StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(new RankingCache { SavedUtc = DateTime.UtcNow, Presets = top.Presets }));
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.Info($"Community ranking unavailable: {ex.Message}");
        }
    }

    /// <summary>Counts one install of an official preset (only if the user allows it). Never throws.</summary>
    public async Task ReportInstallAsync(string presetFileName)
    {
        if (!IsAvailable || !_settings.Current.ShareUsage) return;

        try
        {
            if (string.IsNullOrWhiteSpace(_settings.Current.ClientId))
            {
                _settings.Current.ClientId = Guid.NewGuid().ToString();
                _settings.Save();
            }

            using var response = await _http.PostAsJsonAsync($"{AppInfo.StatsUrl.TrimEnd('/')}/v1/install",
                new { preset = presetFileName, client = _settings.Current.ClientId });
            if (!response.IsSuccessStatusCode)
            {
                _log.Info($"Community ranking: install not counted ({(int)response.StatusCode}).");
                return;
            }

            // Show the new count right away instead of waiting for the next refresh.
            await LoadAsync(force: true);
        }
        catch (Exception ex)
        {
            _log.Info($"Community ranking unavailable: {ex.Message}");
        }
    }

    private static (DateTime SavedUtc, Dictionary<string, int> Counts)? ReadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            var cache = JsonSerializer.Deserialize<RankingCache>(File.ReadAllText(CacheFile));
            return cache?.Presets is null ? null
                : (cache.SavedUtc, new Dictionary<string, int>(cache.Presets, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }
}

internal sealed class TopResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("presets")]
    public Dictionary<string, int>? Presets { get; set; }
}

internal sealed class RankingCache
{
    public DateTime SavedUtc { get; set; }
    public Dictionary<string, int>? Presets { get; set; }
}
