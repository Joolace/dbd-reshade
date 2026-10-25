using System.Diagnostics;
using DBDReshade.Models;

namespace DBDReshade.Services;

/// <summary>
/// One-click update from the GitHub release:
/// <list type="bullet">
/// <item><b>Installed</b> (Inno Setup's uninstaller sits next to the exe): downloads DBDReshade-Setup.exe and
///       runs it silently; Setup closes the app, updates it and starts it again.</item>
/// <item><b>Portable</b>: downloads the new DBDReshade.exe, renames the running exe to .old (Windows allows
///       renaming a running exe), puts the new one in its place and starts it. The .old file is removed
///       on the next start.</item>
/// </list>
/// A Visual Studio build (exe + DBDReshade.dll) never updates itself: the release page opens instead.
/// </summary>
public sealed class UpdateService
{
    private readonly LogService _log;

    public UpdateService(LogService log) => _log = log;

    /// <summary>Passed to the new version after a portable update.</summary>
    public const string UpdatedArgument = "--updated";

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running exe.");

    private static string ExeDirectory => Path.GetDirectoryName(ExePath)!;

    /// <summary>Installed with DBDReshade-Setup.exe (Inno Setup puts unins000.exe in the app folder).</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(ExeDirectory, "unins000.exe"));

    /// <summary>Only the published single-file exe can replace itself.</summary>
    public static bool CanSelfUpdate => !File.Exists(Path.Combine(AppContext.BaseDirectory, "DBDReshade.dll"));

    /// <returns>True when the update is under way and the app must now close; false if this release can't be
    /// installed automatically (the caller then opens the release page).</returns>
    public async Task<bool> InstallAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!CanSelfUpdate)
        {
            _log.Info("Development build: open the release page to update.");
            return false;
        }

        if (IsInstalled && release.SetupUrl is { } setupUrl)
        {
            Directory.CreateDirectory(AppPaths.TempDirectory);
            var setup = Path.Combine(AppPaths.TempDirectory, $"DBDReshade-Setup-{release.Tag}.exe");
            _log.Info($"Downloading the installer of {release.Tag}…");
            await Http.DownloadFileAsync(setupUrl, setup, progress, ct);

            // /SILENT shows only Setup's progress window; Setup closes this app (Restart Manager) and
            // restarts it when done (see [Run] in installer/DBDReshade.iss).
            var start = new ProcessStartInfo(setup) { UseShellExecute = false };
            foreach (var arg in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS" })
                start.ArgumentList.Add(arg);
            Process.Start(start);
            _log.Info($"Installer of {release.Tag} started.");
            return true;
        }

        if (release.PortableUrl is { } exeUrl)
        {
            var exe = ExePath;
            var incoming = exe + ".new";
            var old = exe + ".old";

            _log.Info($"Downloading {release.Tag}…");
            await Http.DownloadFileAsync(exeUrl, incoming, progress, ct);

            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(incoming, exe);
            }
            catch
            {
                File.Move(old, exe); // put the working version back
                throw;
            }

            // The new copy waits for this one to close (see Program.Main).
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = ExeDirectory, Arguments = UpdatedArgument });
            _log.Info($"Updated to {release.Tag}, restarting.");
            return true;
        }

        _log.Warning($"Release {release.Tag} has no {AppInfo.PortableAssetName} / {AppInfo.SetupAssetName} attached.");
        return false;
    }

    /// <summary>Removes the previous exe left behind by a portable update (it was still running then).</summary>
    public static void CleanUpAfterUpdate()
    {
        try
        {
            var old = Environment.ProcessPath + ".old";
            for (var attempt = 0; attempt < 10 && File.Exists(old); attempt++)
            {
                try { File.Delete(old); }
                catch (IOException) { Thread.Sleep(300); } // the old process may still be exiting
                catch (UnauthorizedAccessException) { Thread.Sleep(300); }
            }
        }
        catch
        {
            // Harmless leftover.
        }
    }
}
