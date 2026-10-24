using System.Collections.ObjectModel;
using System.Windows;
using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>Collects log lines for the on-screen log panel and mirrors them to a daily file.</summary>
public sealed class LogService
{
    private const int MaxEntries = 500;
    private readonly object _fileLock = new();

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public void Info(string message) => Add(LogLevel.Info, message);
    public void Success(string message) => Add(LogLevel.Success, message);
    public void Warning(string message) => Add(LogLevel.Warning, message);
    public void Error(string message) => Add(LogLevel.Error, message);

    private void Add(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        WriteToFile(entry);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Append(entry);
        else
            dispatcher.InvokeAsync(() => Append(entry));
    }

    private void Append(LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(0);
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            lock (_fileLock)
                File.AppendAllText(AppPaths.LogFile,
                    $"{entry.Time:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
