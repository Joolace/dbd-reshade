using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Models;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>Settings → Streamer overlay.</summary>
public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly OverlayService _overlay;
    private readonly SettingsService _settings;
    private readonly System.Windows.Threading.DispatcherTimer _nowTimer;

    public OverlayViewModel(OverlayService overlay, SettingsService settings)
    {
        _overlay = overlay;
        _settings = settings;

        var o = Options;
        _enabled = o.Enabled;
        _showPreset = o.ShowPreset;
        _showAuthor = o.ShowAuthor;
        _onlyWhilePlaying = o.OnlyWhilePlaying;
        _isMinimal = o.Style == "minimal";
        _isLight = o.Theme == "light";
        _alignRight = o.AlignRight;
        _writeTextFile = o.WriteTextFile;
        _portText = o.Port.ToString();
        _showInAppPreview = o.ShowInAppPreview;

        // "Now showing: …", refreshed while the settings page is around.
        _nowTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _nowTimer.Tick += (_, _) => RefreshNow();
        _nowTimer.Start();

        Loc.Changed += (_, _) => RefreshStatus();
        _overlay.ApplySettings();
        RefreshStatus();
    }

    private OverlaySettings Options => _settings.Current.Overlay;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _showPreset;
    [ObservableProperty] private bool _showAuthor;
    [ObservableProperty] private bool _onlyWhilePlaying;
    [ObservableProperty] private bool _isMinimal;
    [ObservableProperty] private bool _isLight;
    [ObservableProperty] private bool _alignRight;
    [ObservableProperty] private bool _writeTextFile;
    [ObservableProperty] private string _portText;

    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string? _nowShowing;

    // In-app preview: the same badge the stream shows, drawn by the app.
    [ObservableProperty] private string? _previewTitle;
    [ObservableProperty] private string? _previewAuthor;
    [ObservableProperty] private bool _previewHidden;

    /// <summary>Show the badge preview here in Settings (instead of only in the browser).</summary>
    [ObservableProperty] private bool _showInAppPreview;

    partial void OnShowInAppPreviewChanged(bool value) { Options.ShowInAppPreview = value; Save(); }

    public string Url => _overlay.Url;
    public string TextFilePath => OverlayService.TextFilePath;

    partial void OnEnabledChanged(bool value) { Options.Enabled = value; SaveAndRestart(); }
    partial void OnWriteTextFileChanged(bool value) { Options.WriteTextFile = value; SaveAndRestart(); }
    partial void OnShowPresetChanged(bool value) { Options.ShowPreset = value; Save(); }
    partial void OnShowAuthorChanged(bool value) { Options.ShowAuthor = value; Save(); }
    partial void OnOnlyWhilePlayingChanged(bool value) { Options.OnlyWhilePlaying = value; Save(); }
    partial void OnIsMinimalChanged(bool value) { Options.Style = value ? "minimal" : "card"; Save(); }
    partial void OnIsLightChanged(bool value) { Options.Theme = value ? "light" : "dark"; Save(); }
    partial void OnAlignRightChanged(bool value) { Options.AlignRight = value; Save(); }

    partial void OnPortTextChanged(string value)
    {
        if (!int.TryParse(value, out var port) || port is < 1024 or > 65535 || port == Options.Port) return;
        Options.Port = port;
        OnPropertyChanged(nameof(Url));
        SaveAndRestart();
    }

    private void Save()
    {
        _settings.Save();
        RefreshNow();
    }

    private void SaveAndRestart()
    {
        _settings.Save();
        _overlay.ApplySettings();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        StatusIsError = Enabled && !_overlay.IsRunning;
        Status = !Enabled ? null
            : _overlay.IsRunning ? Loc.Format("Overlay.Running", Url)
            : Loc.Format("Overlay.Error", Options.Port, _overlay.LastError ?? "");
        RefreshNow();
    }

    private void RefreshNow()
    {
        if (!Enabled) { NowShowing = null; return; }
        var (preset, author) = _overlay.CurrentPreset();
        PreviewTitle = ShowPreset && preset is not null ? preset : Loc.Get("Overlay.Tagline");
        PreviewAuthor = ShowPreset && ShowAuthor && preset is not null ? author : null;
        PreviewHidden = _overlay.IsHiddenNow();
        var text = ShowPreset && preset is not null
            ? "DBD Reshade · " + preset + (ShowAuthor && author is not null ? $" ({author})" : "")
            : "DBD Reshade";
        NowShowing = _overlay.IsHiddenNow()
            ? Loc.Get("Overlay.HiddenNow")
            : Loc.Format("Overlay.Now", text);
    }

    [RelayCommand]
    private void CopyUrl()
    {
        try
        {
            System.Windows.Clipboard.SetText(Url);
            Status = Loc.Get("Overlay.Copied");
            StatusIsError = false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard busy (another app has it open): the address is visible anyway.
        }
    }

    [RelayCommand]
    private void OpenPreview() => Shell.Open(Url + "?preview=1");

    [RelayCommand]
    private void ShowTextFile()
    {
        if (File.Exists(TextFilePath)) Shell.ShowInFolder(TextFilePath);
        else
        {
            var folder = Path.GetDirectoryName(TextFilePath)!;
            Directory.CreateDirectory(folder);
            Shell.Open(folder);
        }
    }
}
