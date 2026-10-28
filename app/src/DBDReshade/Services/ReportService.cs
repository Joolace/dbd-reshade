using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace DBDReshade.Services;

public enum ReportOutcome { Sent, TooMany, Failed }

public sealed record ReportResult(ReportOutcome Outcome, string? IssueUrl = null);

/// <summary>
/// "Report a problem": sends the user's description, a few facts about the PC and the app, and (if they
/// want) today's log to the stats worker, which opens a GitHub issue for it. No GitHub account needed.
/// The Windows user name is removed from every path before anything is sent.
/// </summary>
public sealed class ReportService
{
    private const int LogMaxChars = 60_000;

    private readonly LogService _log;
    private readonly Func<string?> _gameInfo;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <param name="gameInfo">The selected game and its ReShade, e.g. "Steam · ReShade 6.4.1" (from the Install page).</param>
    public ReportService(LogService log, Func<string?> gameInfo)
    {
        _log = log;
        _gameInfo = gameInfo;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"DBDReshade/{AppInfo.Version.ToString(3)}");
    }

    /// <summary>The report service is set up (same worker as the ranking).</summary>
    public static bool IsAvailable => !string.IsNullOrWhiteSpace(AppInfo.StatsUrl);

    /// <summary>App version, Windows, language, game: shown to the user before sending.</summary>
    public string SystemInfo()
    {
        var lines = new List<string>
        {
            $"App: DBD Reshade {AppInfo.VersionText}",
            $"Windows: {Environment.OSVersion.Version} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})",
            $"Language: {Loc.Current.Code}",
        };
        try
        {
            if (_gameInfo() is { Length: > 0 } game) lines.Add($"Game: {game}");
        }
        catch (Exception ex)
        {
            lines.Add($"Game: unknown ({ex.GetType().Name})");
        }
        return Anonymize(string.Join(Environment.NewLine, lines));
    }

    /// <summary>The end of today's log, without the Windows user name.</summary>
    public static string ReadLog()
    {
        try
        {
            if (!File.Exists(AppPaths.LogFile)) return "";
            using var stream = new FileStream(AppPaths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            if (text.Length > LogMaxChars) text = "[…]" + Environment.NewLine + text[^LogMaxChars..];
            return Anonymize(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>C:\Users\Mario\... → C:\Users\&lt;user&gt;\... (and the name anywhere else in the text).</summary>
    public static string Anonymize(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0)
            text = text.Replace(profile, Path.Combine(Path.GetDirectoryName(profile) ?? "", "<user>"), StringComparison.OrdinalIgnoreCase);
        var user = Environment.UserName;
        if (user.Length >= 3)
            text = text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    public async Task<ReportResult> SendAsync(string description, string? contact, bool includeLog, CancellationToken ct = default)
    {
        if (!IsAvailable) return new ReportResult(ReportOutcome.Failed);
        try
        {
            var payload = new
            {
                description = Anonymize(description.Trim()),
                contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim(),
                system = SystemInfo(),
                log = includeLog ? ReadLog() : null,
            };
            using var response = await _http.PostAsJsonAsync($"{AppInfo.StatsUrl.TrimEnd('/')}/v1/report", payload, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return new ReportResult(ReportOutcome.TooMany);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning($"Report not sent: HTTP {(int)response.StatusCode}");
                return new ReportResult(ReportOutcome.Failed);
            }
            var created = await response.Content.ReadFromJsonAsync<CreatedIssue>(cancellationToken: ct);
            _log.Success($"Report sent: {created?.Url}");
            return new ReportResult(ReportOutcome.Sent, created?.Url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.Warning($"Report not sent: {ex.Message}");
            return new ReportResult(ReportOutcome.Failed);
        }
    }

    /// <summary>Plan B (service unreachable): GitHub's "new issue" page, already filled in (needs a GitHub account).</summary>
    public string GitHubIssueUrl(string description)
    {
        var body = $"### What happened\n{Anonymize(description.Trim())}\n\n### System\n```\n{SystemInfo()}\n```\n\n_Please attach today's log from the app's Logs folder._";
        if (body.Length > 6000) body = body[..6000];
        return $"{AppInfo.RepoUrl}/issues/new?labels=bug-report&title={Uri.EscapeDataString("[Report] ")}&body={Uri.EscapeDataString(body)}";
    }

    private sealed class CreatedIssue
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
