using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>One release in the "What's new" window.</summary>
public sealed class ReleaseItem
{
    public ReleaseItem(ReleaseNotes notes)
    {
        Notes = notes;
        var version = notes.Version;
        IsCurrent = version is not null && version.ToString(3) == AppInfo.Version.ToString(3);
        IsNewer = version is not null && version > AppInfo.Version;
    }

    public ReleaseNotes Notes { get; }
    public string Title => Notes.Name;
    public string Body => Notes.Body;
    public string Url => Notes.Url;

    /// <summary>The version running now (highlighted).</summary>
    public bool IsCurrent { get; }

    /// <summary>Newer than the version running now: an update is available.</summary>
    public bool IsNewer { get; }

    public string Date
    {
        get
        {
            if (Notes.Published == DateTimeOffset.MinValue) return "";
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(Loc.Current.Code); }
            catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
            var date = Notes.Published.ToLocalTime().DateTime;
            return culture.TwoLetterISOLanguageName is "ja" or "zh" or "ko"
                ? date.ToString("D", culture)
                : date.ToString("d MMMM yyyy", culture);
        }
    }
}

/// <summary>The "What's new" window: the notes of the latest GitHub releases.</summary>
public sealed partial class WhatsNewViewModel : ObservableObject
{
    private readonly ChangelogService _changelog;

    public WhatsNewViewModel(ChangelogService changelog, bool justUpdated)
    {
        _changelog = changelog;
        Subtitle = justUpdated
            ? Loc.Format("WhatsNew.Updated", AppInfo.VersionText)
            : Loc.Get("WhatsNew.Subtitle");
    }

    public string Subtitle { get; }

    public ObservableCollection<ReleaseItem> Releases { get; } = new();

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasError;

    public event EventHandler? RequestClose;

    public async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        try
        {
            var releases = await _changelog.LoadAsync();
            Releases.Clear();
            foreach (var release in releases) Releases.Add(new ReleaseItem(release));
            HasError = Releases.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenReleases() => Shell.Open(AppInfo.ReleasesUrl);

    [RelayCommand]
    private void OpenLink(string? url) => Shell.Open(url);

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);
}
