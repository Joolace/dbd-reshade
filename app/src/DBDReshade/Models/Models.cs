using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using DBDReshade.Services;

namespace DBDReshade.Models;

/// <summary>A Dead by Daylight installation found on this PC.</summary>
/// <param name="Store">"Steam", "Epic" or "Xbox" (Microsoft Store / PC Game Pass).</param>
/// <param name="RootPath">The game folder; for Xbox, its "Content" folder.</param>
public sealed record GameInstallation(string Store, string RootPath)
{
    public const string XboxLauncher = "gamelaunchhelper.exe";

    public bool IsXbox => Store == "Xbox";

    /// <summary>
    /// Folder ReShade goes in (with ReShade.ini and reshade-shaders): next to the exe ReShade hooks.
    /// Steam: Binaries\Win64. Epic: Binaries\EGS. Xbox: the Content folder, next to gamelaunchhelper.exe:
    /// the real game exe in Binaries\WinGDK is protected by the Xbox app and ReShade can't be installed there.
    /// </summary>
    public string BinariesDirectory
    {
        get
        {
            if (IsXbox) return RootPath;

            var binaries = System.IO.Path.Combine(RootPath, "DeadByDaylight", "Binaries");
            var egs = System.IO.Path.Combine(binaries, "EGS");
            var win64 = System.IO.Path.Combine(binaries, "Win64");

            if (Store == "Epic" && Directory.Exists(egs)) return egs;
            if (Directory.Exists(win64)) return win64;
            return Directory.Exists(egs) ? egs : win64;
        }
    }

    /// <summary>The exe ReShade is installed for: *-Shipping.exe, or gamelaunchhelper.exe for Xbox.</summary>
    public string? GameExecutable
    {
        get
        {
            if (!Directory.Exists(BinariesDirectory)) return null;
            if (IsXbox)
            {
                var helper = System.IO.Path.Combine(BinariesDirectory, XboxLauncher);
                return File.Exists(helper) ? helper : null;
            }
            return Directory.EnumerateFiles(BinariesDirectory, "*-Shipping.exe").FirstOrDefault();
        }
    }

    public string ReShadeIniPath => System.IO.Path.Combine(BinariesDirectory, "ReShade.ini");

    public string DisplayName => $"{Store}  ·  {RootPath}";
}

/// <summary>One entry of presets.json.</summary>
public sealed class PresetMetadata
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Where the preset comes from (video, site, Discord…). Stored as "videoLink" for compatibility.</summary>
    [JsonPropertyName("videoLink")]
    public string? Source { get; set; }

    [JsonPropertyName("preset-preview")]
    public string? PreviewImage { get; set; }

    /// <summary>Who made the preset (used by the author filter).</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>The map/realm the preset is made for; empty = all maps (used by the map filter).</summary>
    [JsonPropertyName("map")]
    public string? Map { get; set; }

    /// <summary>Style labels: "bright", "dark", "competitive", "cinematic" (used by the style filter).</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}

/// <summary>A preset .ini available locally.</summary>
public sealed partial class Preset : ObservableObject
{
    public Preset(string filePath, PresetMetadata? metadata, bool isCustom)
    {
        FilePath = filePath;
        Metadata = metadata;
        IsCustom = isCustom;
    }

    public string FilePath { get; }
    public PresetMetadata? Metadata { get; }
    public bool IsCustom { get; }

    /// <summary>Starred by the user (saved in settings).</summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>Arrived with a recent sync (shown with a "New" badge for a few days).</summary>
    public bool IsNew { get; init; }

    /// <summary>Installs counted by the community ranking (0 = none or ranking off).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallsText))]
    private int _installs;

    /// <summary>Among the most installed presets: shown with a 🔥 badge.</summary>
    [ObservableProperty]
    private bool _isPopular;

    /// <summary>"🔥 950", "🔥 1.2k".</summary>
    public string InstallsText => "🔥 " + (Installs >= 1000 ? (Installs / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k" : Installs.ToString());

    /// <summary>Estimated FPS cost (light / medium / heavy) from the effects the preset turns on.</summary>
    public PerformanceInfo Performance => PerformanceRating.Rate(FilePath);

    public PerformanceLevel PerformanceLevel => Performance.Level;

    public string PerformanceText => Loc.Get($"Perf.{Performance.Level}");

    public string PerformanceTip => Performance.HeaviestEffects.Count == 0
        ? Loc.Get("Perf.TipLight")
        : Loc.Format("Perf.Tip", string.Join(", ", Performance.HeaviestEffects));

    public string? Author => string.IsNullOrWhiteSpace(Metadata?.Author) ? null : Metadata!.Author!.Trim();

    public string? Map => string.IsNullOrWhiteSpace(Metadata?.Map) ? null : Metadata!.Map!.Trim();

    public IReadOnlyList<string> Tags =>
        Metadata?.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).ToList()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>File name including extension, e.g. "01.STX.Dvarka.ini" (the presets.json key).</summary>
    public string FileName => System.IO.Path.GetFileName(FilePath);

    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(FilePath);

    public string Description =>
        string.IsNullOrWhiteSpace(Metadata?.Description) ? Loc.Get("Preset.NoDescription") : Metadata!.Description!;

    /// <summary>The preset's source link, only if it is a valid http(s) address.</summary>
    public string? SourceLink =>
        Uri.TryCreate(Metadata?.Source, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;

    public override string ToString() => DisplayName;
}

/// <summary>One entry of a filter list: Value null means "all".</summary>
public sealed record FilterOption(string? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Preset wrapper with a checkbox, used by the copy page.</summary>
public sealed partial class SelectablePreset : ObservableObject
{
    public SelectablePreset(Preset preset) => Preset = preset;

    public Preset Preset { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <param name="PortableUrl">Download link of DBDReshade.exe attached to the release, if any.</param>
/// <param name="SetupUrl">Download link of DBDReshade-Setup.exe attached to the release, if any.</param>
public sealed record ReleaseInfo(string Tag, string Url, Version? Version, string? PortableUrl = null, string? SetupUrl = null);

public sealed record RemoteFile(string Name, string DownloadUrl, string Sha);

public sealed record SyncResult(int NewPresets, int UpdatedPresets, int RefreshedPreviews)
{
    public int ChangedPresets => NewPresets + UpdatedPresets;
}

public enum LogLevel { Info, Success, Warning, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>Settings persisted to settings.json between runs.</summary>
public sealed class AppSettings
{
    /// <summary>Folder chosen by the user for preset files; null = use the game's binaries folder.</summary>
    public string? CustomPresetFolder { get; set; }

    /// <summary>Root path of the installation picked last time.</summary>
    public string? LastGamePath { get; set; }

    /// <summary>Presets the user deleted, so the GitHub sync doesn't bring them back.</summary>
    public List<string> IgnoredPresets { get; set; } = new();

    /// <summary>UI language code ("en", "it"…); null = follow Windows.</summary>
    public string? Language { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Preset file names the user starred.</summary>
    public List<string> Favorites { get; set; } = new();

    /// <summary>The last preset installed in each game folder (key: game root path), to notice when a game update breaks it.</summary>
    public Dictionary<string, InstallRecord> Installs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Overlay for streamers (OBS browser source).</summary>
    public OverlaySettings Overlay { get; set; } = new();

    /// <summary>Keyboard shortcuts that switch preset in game (ReShade's PresetShortcutKeys).</summary>
    public List<PresetHotkey> Hotkeys { get; set; } = new();

    /// <summary>When "Download everything for offline use" last finished (UTC).</summary>
    public DateTime? OfflineDownloadedUtc { get; set; }

    /// <summary>Last version whose "What's new" was shown: after an update, the window opens once on start.</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>Send anonymous install counts to the community ranking (Settings, on by default).</summary>
    public bool ShareUsage { get; set; } = true;

    /// <summary>Random id of this installation for the ranking (counted once per preset; stored hashed on the server).</summary>
    public string? ClientId { get; set; }

    /// <summary>Presets that arrived with a sync, and when (they get a "New" badge for a few days).</summary>
    public Dictionary<string, DateTime> NewPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>What the app installed in a game folder, checked on start to catch a game update that undid it.</summary>
public sealed class InstallRecord
{
    public string PresetFileName { get; set; } = "";

    /// <summary>Full path of the preset copy ReShade uses.</summary>
    public string PresetPath { get; set; } = "";

    /// <summary>Size and date of the game exe when installed: a change means the game was updated.</summary>
    public string? GameStamp { get; set; }
}

/// <summary>A key (Windows virtual-key code plus modifiers) that switches to a preset in game.</summary>
public sealed class PresetHotkey
{
    public string PresetFileName { get; set; } = "";
    public int Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
}

/// <summary>Settings of the streamer overlay (Settings → Streamer overlay).</summary>
public sealed class OverlaySettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 7373;
    public bool ShowPreset { get; set; } = true;
    public bool ShowAuthor { get; set; } = true;
    /// <summary>Hide the overlay while Dead by Daylight is closed.</summary>
    public bool OnlyWhilePlaying { get; set; } = true;
    /// <summary>"card" or "minimal".</summary>
    public string Style { get; set; } = "card";
    /// <summary>"dark" or "light".</summary>
    public string Theme { get; set; } = "dark";
    public bool AlignRight { get; set; }
    /// <summary>Show the overlay preview inside Settings.</summary>
    public bool ShowInAppPreview { get; set; } = true;
    /// <summary>Also write the text to a file, for a "Text (GDI+)" source.</summary>
    public bool WriteTextFile { get; set; }
}
