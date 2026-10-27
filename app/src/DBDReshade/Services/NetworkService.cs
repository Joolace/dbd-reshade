using System.Net.NetworkInformation;

namespace DBDReshade.Services;

/// <summary>
/// Knows whether the PC is online. Windows says when a network comes or goes, but being on a network doesn't
/// mean having internet (captive Wi-Fi, a cable with no router...), so every change is confirmed with a tiny
/// request to Windows' own connectivity test page. While offline it checks again every 30 seconds.
/// </summary>
public sealed class NetworkService : IDisposable
{
    /// <summary>The page Windows itself uses to tell if there's internet (a few bytes of text).</summary>
    private const string ProbeUrl = "http://www.msftconnecttest.com/connecttest.txt";
    private const string ProbeFallbackUrl = "https://api.github.com/zen";

    private readonly LogService _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly System.Threading.Timer _retry;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NetworkService(LogService log)
    {
        _log = log;
        _retry = new System.Threading.Timer(_ => { if (!IsOnline) _ = CheckAsync(); });
        NetworkChange.NetworkAvailabilityChanged += (_, _) => _ = CheckAsync();
        NetworkChange.NetworkAddressChanged += (_, _) => _ = CheckAsync();
    }

    /// <summary>Last known state (true until the first check says otherwise).</summary>
    public bool IsOnline { get; private set; } = true;

    /// <summary>Raised when the state changes, with the new state. Not on the UI thread.</summary>
    public event EventHandler<bool>? Changed;

    /// <summary>Checks the connection now; returns whether the PC is online.</summary>
    public async Task<bool> CheckAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var online = NetworkInterface.GetIsNetworkAvailable() && (await ProbeAsync(ProbeUrl) || await ProbeAsync(ProbeFallbackUrl));
            if (online != IsOnline)
            {
                IsOnline = online;
                if (online) _log.Success("Back online.");
                else _log.Warning("No internet connection: working offline.");
                Changed?.Invoke(this, online);
            }
            // While offline, look again every 30 seconds (network events don't cover every case).
            _retry.Change(online ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
            return online;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ProbeAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return true; // any answer at all means there's a way out
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _retry.Dispose();
        _http.Dispose();
    }
}
