using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBDReshade.Helpers;
using DBDReshade.Services;

namespace DBDReshade.ViewModels;

/// <summary>"Report a problem" window: the report becomes a GitHub issue through the stats worker.</summary>
public sealed partial class ReportViewModel : ObservableObject
{
    private const int MinDescription = 15;

    private readonly ReportService _report;

    public ReportViewModel(ReportService report, string? prefill)
    {
        _report = report;
        _description = prefill ?? "";
        SystemInfo = report.SystemInfo();
        RefreshPreview();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(CharactersLeft))]
    private string _description;

    [ObservableProperty] private string? _contact;

    [ObservableProperty] private bool _includeLog = true;

    partial void OnIncludeLogChanged(bool value) => RefreshPreview();

    public string SystemInfo { get; }

    /// <summary>Exactly what will be sent besides the description (system info and the log).</summary>
    [ObservableProperty] private string _preview = "";

    /// <summary>"10 more characters" while the description is too short to send.</summary>
    public string? CharactersLeft => Description.Trim().Length >= MinDescription
        ? null
        : Loc.Format("Report.MoreChars", MinDescription - Description.Trim().Length);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSending;

    // After sending: one of these is shown instead of the form.
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private string? _resultTitle;
    [ObservableProperty] private string? _resultText;
    [ObservableProperty] private string? _issueUrl;
    [ObservableProperty] private bool _showFallback;

    /// <summary>"What will be sent" is open.</summary>
    [ObservableProperty] private bool _showPreview;

    [RelayCommand]
    private void TogglePreview() => ShowPreview = !ShowPreview;

    public event EventHandler? RequestClose;

    private void RefreshPreview()
    {
        var log = IncludeLog ? ReportService.ReadLog() : "";
        Preview = SystemInfo + Environment.NewLine + Environment.NewLine
                  + (IncludeLog ? (log.Length > 0 ? log : Loc.Get("Report.LogEmpty")) : Loc.Get("Report.NoLog"));
    }

    private bool CanSend() => !IsSending && Description.Trim().Length >= MinDescription;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        IsSending = true;
        try
        {
            var result = await _report.SendAsync(Description, Contact, IncludeLog);
            IsDone = true;
            switch (result.Outcome)
            {
                case ReportOutcome.Sent:
                    ResultTitle = Loc.Get("Report.SentTitle");
                    ResultText = Loc.Get("Report.SentText");
                    IssueUrl = result.IssueUrl;
                    ShowFallback = false;
                    break;
                case ReportOutcome.TooMany:
                    ResultTitle = Loc.Get("Report.TooManyTitle");
                    ResultText = Loc.Get("Report.TooManyText");
                    ShowFallback = true;
                    break;
                default:
                    ResultTitle = Loc.Get("Report.FailedTitle");
                    ResultText = Loc.Get("Report.FailedText");
                    ShowFallback = true;
                    break;
            }
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void OpenIssue() => Shell.Open(IssueUrl);

    /// <summary>Plan B: GitHub's new-issue page filled in, plus the logs folder to attach the file.</summary>
    [RelayCommand]
    private void OpenGitHub()
    {
        Shell.Open(_report.GitHubIssueUrl(Description));
        Shell.Open(AppPaths.LogsDirectory);
    }

    [RelayCommand]
    private void OpenDiscord()
    {
        Shell.Open(AppInfo.DiscordUrl);
        Shell.Open(AppPaths.LogsDirectory);
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);
}
