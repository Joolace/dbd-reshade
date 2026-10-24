using WinForms = System.Windows.Forms;

namespace DBDReshade.Viewer;

/// <summary>
/// Entry point of the map preview mode (formerly the D3Daylight app):
/// <c>DBDReshade.exe --viewer "&lt;folder with screenshots&gt;" [--title "&lt;map name&gt;"] [--hint "&lt;help text&gt;"]</c>
/// </summary>
public static class ViewerApp
{
    public const string Argument = "--viewer";

    public static int Run(string[] args)
    {
        var folder = args.Length > 1 ? args[1] : null;
        var title = ReadOption(args, "--title") ?? (folder is null ? "Preview" : Path.GetFileName(folder));
        var hint = ReadOption(args, "--hint") ?? "← → change image · ESC close";

        WinForms.Application.SetHighDpiMode(WinForms.HighDpiMode.PerMonitorV2);
        WinForms.Application.EnableVisualStyles();
        WinForms.Application.SetCompatibleTextRenderingDefault(false);

        var images = folder is not null && Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        if (images.Count == 0)
        {
            WinForms.MessageBox.Show($"No screenshots found in:\n{folder}", "DBD Reshade",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            return 1;
        }

        WinForms.Application.Run(new PreviewWindow(images, title, hint));
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
