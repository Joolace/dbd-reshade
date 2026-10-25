using DBDReshade.Services;
using DBDReshade.Viewer;

namespace DBDReshade;

/// <summary>
/// One exe, two modes:
/// <list type="bullet">
/// <item>normal start → the WPF app;</item>
/// <item><c>--viewer &lt;folder&gt;</c> → the Direct3D 11 map preview (formerly D3Daylight).</item>
/// </list>
/// The preview runs from a copy of this exe in its own folder, where ReShade is installed, so
/// ReShade only ever hooks the preview window and never the app's own interface.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == ViewerApp.Argument)
            return ViewerApp.Run(args);

        // One copy at a time: two would sync presets into the same folders and share one settings file.
        using var mutex = new Mutex(false, @"Local\DBDReshade.SingleInstance");
        if (!TryAcquire(mutex, args.Contains(UpdateService.UpdatedArgument) ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(2)))
        {
            ActivateRunningCopy();
            return 0;
        }

        try
        {
            UpdateService.CleanUpAfterUpdate();

            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Waits a little: right after a portable update the previous version is still closing
    /// (it starts the new one first), so the new one waits longer for it.
    /// </summary>
    private static bool TryAcquire(Mutex mutex, TimeSpan wait)
    {
        try
        {
            return mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            return true; // the previous copy crashed: the mutex is ours now
        }
    }

    /// <summary>Brings the window of the copy that's already open to the front.</summary>
    private static void ActivateRunningCopy()
    {
        try
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var other in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
            {
                using (other)
                {
                    if (other.Id == current.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                    SetForegroundWindow(other.MainWindowHandle);
                    return;
                }
            }
        }
        catch
        {
            // Nothing to bring forward: the other copy is still starting or closing.
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
