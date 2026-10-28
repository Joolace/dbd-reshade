using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>
/// Overlay for streamers: a small web page OBS (or Streamlabs) shows as a "Browser source", saying the
/// stream uses DBD Reshade and, if wanted, which preset is active. The app serves it on this PC only
/// (http://localhost:PORT); the preset is read from ReShade.ini, so switching preset in game (overlay or
/// shortcut keys) updates it within a couple of seconds. It can also write a plain text file for a
/// "Text (GDI+)" source.
/// </summary>
public sealed class OverlayService : IDisposable
{
    public const int DefaultPort = 7373;

    private readonly SettingsService _settings;
    private readonly PresetRepository _repository;
    private readonly LogService _log;
    private readonly Func<GameInstallation?> _game;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private System.Threading.Timer? _textTimer;

    // Whether the game is running is checked at most every few seconds (it scans the process list).
    private DateTime _runningCheckedAt;
    private bool _running;

    public OverlayService(SettingsService settings, PresetRepository repository, LogService log, Func<GameInstallation?> game)
    {
        _settings = settings;
        _repository = repository;
        _log = log;
        _game = game;
        // Read on the UI thread: the web server runs on background threads, where resources can't be read.
        _tagline = Loc.Get("Overlay.Tagline");
        Loc.Changed += (_, _) => _tagline = Loc.Get("Overlay.Tagline");
    }

    private volatile string _tagline;

    private OverlaySettings Options => _settings.Current.Overlay;

    public bool IsRunning => _listener?.IsListening == true;

    public string Url => $"http://localhost:{Options.Port}/";

    /// <summary>Why the overlay couldn't start (e.g. the port is taken), or null.</summary>
    public string? LastError { get; private set; }

    public static string TextFilePath => Path.Combine(AppPaths.DataRoot, "Overlay", "current-preset.txt");

    /// <summary>Starts or stops the server and the text file to match the settings.</summary>
    public void ApplySettings()
    {
        Stop();
        if (!Options.Enabled) return;
        Start();
    }

    private void Start()
    {
        LastError = null;
        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{Options.Port}/");
            listener.Prefixes.Add($"http://127.0.0.1:{Options.Port}/");
            // Loaded here, on the UI thread, then served from memory.
            Resource("Assets/logo.png");
            Resource("Assets/Fonts/Montserrat-Regular.ttf");
            listener.Start();
            _listener = listener;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ServeAsync(listener, _cts.Token));
            _log.Info($"Streamer overlay running at {Url}");
        }
        catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
        {
            LastError = ex.Message;
            _listener = null;
            _log.Warning($"Streamer overlay could not start on port {Options.Port}: {ex.Message}");
        }

        if (Options.WriteTextFile)
            _textTimer = new System.Threading.Timer(_ => WriteTextFile(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    private void Stop()
    {
        _textTimer?.Dispose();
        _textTimer = null;
        _cts?.Cancel();
        try { _listener?.Stop(); _listener?.Close(); } catch (ObjectDisposedException) { }
        _listener = null;
    }

    private async Task ServeAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested || !listener.IsListening)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Warning($"Streamer overlay: {ex.Message}");
                continue;
            }
            _ = Task.Run(() => Handle(context), ct);
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            response.Headers["Cache-Control"] = "no-store";
            switch (context.Request.Url?.AbsolutePath.ToLowerInvariant())
            {
                case "/" or "/index.html":
                    Write(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page.Value));
                    break;
                case "/status":
                    Write(response, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(Status()));
                    break;
                case "/logo.png":
                    Write(response, "image/png", Resource("Assets/logo.png"));
                    break;
                case "/font.ttf":
                    Write(response, "font/ttf", Resource("Assets/Fonts/Montserrat-Regular.ttf"));
                    break;
                default:
                    response.StatusCode = 404;
                    response.Close();
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
            // OBS closed the connection: nothing to do.
        }
        catch (Exception ex)
        {
            // Anything else would leave OBS waiting on a blank page: answer with the error and log it.
            _log.Warning($"Streamer overlay: {context.Request.Url?.AbsolutePath} failed: {ex.Message}");
            try
            {
                response.StatusCode = 500;
                Write(response, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("DBD Reshade overlay error: " + ex.Message));
            }
            catch
            {
                // The connection is gone.
            }
        }
    }

    private static void Write(HttpListenerResponse response, string type, byte[] body)
    {
        response.ContentType = type;
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body);
        response.Close();
    }

    /// <summary>What the overlay page shows right now.</summary>
    public object Status()
    {
        var (preset, author) = CurrentPreset();
        var running = IsGameRunning();
        return new
        {
            label = "DBD Reshade",
            tagline = _tagline,
            preset,
            author,
            showPreset = Options.ShowPreset,
            showAuthor = Options.ShowAuthor,
            style = Options.Style,
            theme = Options.Theme,
            align = Options.AlignRight ? "right" : "left",
            visible = !Options.OnlyWhilePlaying || running,
        };
    }

    /// <summary>The preset ReShade is using (from ReShade.ini) and its author, if known.</summary>
    public (string? Name, string? Author) CurrentPreset()
    {
        if (_game() is not { } game) return (null, null);
        var path = IniFile.GetValue(game.ReShadeIniPath, "GENERAL", "PresetPath");
        if (string.IsNullOrWhiteSpace(path)) return (null, null);

        var fileName = Path.GetFileName(path.Trim().Trim('"'));
        var preset = _repository.Presets.FirstOrDefault(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        return (Path.GetFileNameWithoutExtension(fileName), preset?.Author);
    }

    /// <summary>Hidden right now because the game is closed (and "only while playing" is on).</summary>
    public bool IsHiddenNow() => Options.OnlyWhilePlaying && !IsGameRunning();

    private bool IsGameRunning()
    {
        if (DateTime.UtcNow - _runningCheckedAt < TimeSpan.FromSeconds(3)) return _running;
        _runningCheckedAt = DateTime.UtcNow;
        try
        {
            _running = _game() is { } game && ReShadeService.IsGameRunning(game);
        }
        catch
        {
            _running = false;
        }
        return _running;
    }

    /// <summary>"DBD Reshade · Koda - Coldwind Farm" for a Text source; empty while the game is closed (if so chosen).</summary>
    private void WriteTextFile()
    {
        try
        {
            var (preset, _) = CurrentPreset();
            var visible = !Options.OnlyWhilePlaying || IsGameRunning();
            var text = !visible ? ""
                : Options.ShowPreset && preset is not null ? $"DBD Reshade · {preset}"
                : "DBD Reshade";
            Directory.CreateDirectory(Path.GetDirectoryName(TextFilePath)!);
            if (!File.Exists(TextFilePath) || File.ReadAllText(TextFilePath) != text)
                File.WriteAllText(TextFilePath, text, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // OBS may be reading it at this very moment: try again on the next tick.
        }
    }

    private static readonly Lazy<string> Page = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("overlay.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private static readonly Dictionary<string, byte[]> Resources = new();

    private static byte[] Resource(string path)
    {
        lock (Resources)
        {
            if (Resources.TryGetValue(path, out var cached)) return cached;
            var info = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/{path}"));
            using var stream = info!.Stream;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return Resources[path] = memory.ToArray();
        }
    }

    public void Dispose() => Stop();
}
